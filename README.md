# PayLedger

[![CI](https://github.com/qaweem321/payledger/actions/workflows/ci.yml/badge.svg)](https://github.com/qaweem321/payledger/actions/workflows/ci.yml)

A payments and wallet REST API built with **ASP.NET Core (.NET 10)**, **Entity Framework Core** and **SQL Server**.
Merchants open wallets in several currencies, take deposits, pay out, and move money between wallets, including
across currencies at a live exchange rate. Every movement is recorded with double-entry bookkeeping, every
request can be retried safely, and merchants get signed webhooks when money moves.

## What it shows

| Area | How |
| --- | --- |
| Web API | Controllers, `[ApiController]` validation, RFC 9457 problem details with stable error `code`s, OpenAPI + Scalar docs at `/docs` |
| Clean architecture, SOLID | `Domain` (rules, no dependencies) → `Application` (use cases) → `Infrastructure` (EF Core, HTTP) → `Api` |
| EF Core + SQL Server | Fluent configurations, migrations, check constraints, filtered unique indexes, `rowversion`, keyset paging |
| Correct money handling | Integer minor units, double-entry ledger, `UPDLOCK` row locks in a fixed order, rounding that never creates money |
| Safe retries | `Idempotency-Key` stored in the same transaction as the payment; parallel retries return one result |
| Third-party integration | Live FX rates over `HttpClient` with retries, timeouts and a circuit breaker (`Microsoft.Extensions.Http.Resilience`), cached |
| Reliable webhooks | Transactional outbox, background sender that scales to several instances (`READPAST`), HMAC signatures, exponential backoff, SSRF protection |
| Security | Hashed API keys, per-merchant data isolation, rate limits, no-store and nosniff headers |
| Testing | xUnit v3 unit tests and integration tests against a real SQL Server (Testcontainers), including parallel race tests |
| Delivery | Docker (chiseled, non-root image), Docker Compose, GitHub Actions CI, Conventional Commits |

## Quick start (Docker)

Needs Docker with Compose. SQL Server needs about 2 GB of RAM.

```bash
git clone https://github.com/qaweem321/payledger.git && cd payledger
```

```bash
echo "MSSQL_SA_PASSWORD=Pl-$(openssl rand -hex 16)" > .env
```

```bash
docker compose up -d --build
```

Open <http://localhost:5080/docs> for the interactive API reference. The database schema is created on start.

## Try it with curl

There is no demo login: every merchant signs up through the API and gets its own API key.
The commands below save values in shell variables, so run them in one terminal, in order (they use `jq`).

```bash
KEY=$(curl -s localhost:5080/v1/merchants -H 'Content-Type: application/json' \
  -d '{"name":"Demo Shop","email":"demo@example.com"}' | jq -r .apiKey)
```

```bash
USD=$(curl -s localhost:5080/v1/accounts -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d '{"name":"Main USD","currency":"USD"}' | jq .id)
PKR=$(curl -s localhost:5080/v1/accounts -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d '{"name":"Karachi PKR","currency":"PKR"}' | jq .id)
```

Deposit $100.00 (amounts are integers in minor units, so 10000 cents). Run it twice: the `Idempotency-Key`
makes the second call return the same transaction with `Idempotent-Replayed: true` instead of paying twice.

```bash
curl -i localhost:5080/v1/deposits -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: order-1001' -d "{\"accountId\":$USD,\"amount\":10000}"
```

Move $25.00 into the PKR wallet at the live rate, then look at the ledger:

```bash
curl -s localhost:5080/v1/transfers -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d "{\"sourceAccountId\":$USD,\"destinationAccountId\":$PKR,\"amount\":2500}" | jq
```

```bash
curl -s localhost:5080/v1/accounts -H "Authorization: Bearer $KEY" | jq '.data[] | {name, currency, balance}'
```

## API overview

All endpoints except signup, `/health/*` and the docs need `Authorization: Bearer <api key>`.

| Method | Path | What it does |
| --- | --- | --- |
| POST | `/v1/merchants` | Sign up; returns the API key once (10 sign-ups per hour per IP) |
| GET | `/v1/merchants/me` | Your merchant profile |
| POST | `/v1/merchants/me/api-key` | Replace the API key; the old one stops working at once |
| PUT / DELETE | `/v1/merchants/me/webhook` | Set (returns a signing secret once) or remove the webhook URL |
| POST | `/v1/accounts` | Open a wallet in USD, EUR, GBP, PKR, AED, SAR, JPY or KWD |
| GET | `/v1/accounts`, `/v1/accounts/{id}` | Wallets and settlement accounts with balances |
| GET | `/v1/accounts/{id}/entries` | Ledger entries of one account, each with the balance after it |
| POST | `/v1/deposits` | Money in from outside (bank transfer, card) |
| POST | `/v1/withdrawals` | Money out (payout); fails with `insufficient_funds` if the balance is too low |
| POST | `/v1/transfers` | Wallet to wallet; converts at the live rate when currencies differ |
| GET | `/v1/transactions`, `/v1/transactions/{id}` | Transactions with their ledger entries; filter by `accountId` or `type` |
| GET | `/v1/fx/quote?from=USD&to=PKR&amount=10000` | What a conversion would give right now |
| GET | `/health/live`, `/health/ready` | Liveness, and readiness including the database |

Lists return `{ "data": [...], "hasMore": true }`, newest first. Pass `limit` (1-100) and `startingAfter=<last id>`
for the next page. Errors are problem details with a `code`, for example `insufficient_funds`,
`idempotency_key_reused`, `unsupported_currency`, `exchange_rate_unavailable`.

## How money moves

Each merchant gets one **settlement account** per currency. It stands for the outside world (bank, card network,
FX desk), so it may go negative. Wallets never go negative: the domain refuses it and a database check constraint
backs that up.

A transfer of $25.00 into a PKR wallet at 278.45 writes four entries, and each currency adds up to zero:

| Account | Currency | Amount |
| --- | --- | --- |
| Main USD (wallet) | USD | -2,500 |
| USD settlement | USD | +2,500 |
| PKR settlement | PKR | -696,125 |
| Karachi PKR (wallet) | PKR | +696,125 |

Conversions are rounded down, so rounding never creates money. Amounts too small to convert are refused.

## Webhooks

After `PUT /v1/merchants/me/webhook`, every completed transaction is sent as `POST` with a JSON body
(`{ id, type: "transaction.completed", createdAt, data }`) and these headers:

- `PayLedger-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256 of "<t>.<raw body>" with your secret>`
- `PayLedger-Event`, `PayLedger-Delivery` (use the delivery id to ignore duplicates)

Failed deliveries are retried with exponential backoff (30 s, 1 min, 2 min ... up to 6 hours, 8 attempts).
Verify a request in C#:

```csharp
var ok = PayLedger.Infrastructure.Webhooks.WebhookSigner.Verify(
    secret, request.Headers["PayLedger-Signature"]!, rawBody, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
```

Webhook URLs must be `https` and resolve to public addresses (checked again at connect time, so DNS tricks
don't help). For local testing, `Webhooks__AllowHttp=true` and `Webhooks__AllowPrivateNetworks=true` relax this.

## Configuration

Settings come from `appsettings.json` and can be overridden with environment variables (`Section__Key`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings__Default` | none (required) | SQL Server connection string; the app stops at start-up without it |
| `MSSQL_SA_PASSWORD` (`.env`, Docker only) | none (required) | 8+ characters with three of: upper, lower, digit, symbol. Generate: `echo "Pl-$(openssl rand -hex 16)"` |
| `Database__MigrateOnStartup` | `false` (`true` in Docker and Development) | Apply EF Core migrations when the app starts |
| `ExchangeRates__BaseUrl` | `https://open.er-api.com/v6/` | ExchangeRate-API compatible rate service (free, no key) |
| `ExchangeRates__CacheMinutes` | `60` | How long one rate table is reused |
| `Webhooks__DispatcherEnabled` | `true` | Run the background webhook sender in this instance |
| `Webhooks__AllowHttp`, `Webhooks__AllowPrivateNetworks` | `false` | Local testing only (see above) |
| `RateLimits__RequestsPerMinute` | `300` | Per merchant (per IP before sign-in) |
| `RateLimits__SignupsPerHour` | `10` | Per IP |
| `ReverseProxy__KnownNetworks__0` | empty | CIDR of your reverse proxy, e.g. `10.0.0.0/8`. Only then is `X-Forwarded-For` trusted (last hop only), so rate limits see real client IPs |

Exchange rates are provided by [ExchangeRate-API](https://www.exchangerate-api.com) under its open access terms.

## Run without Docker for the API

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), start only the database, then run the API
(the Development settings point at `localhost:1433` with the password in `appsettings.Development.json`):

```bash
docker run -d --name payledger-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=PayLedger-Dev-2026!' -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04
```

```bash
dotnet run --project src/PayLedger.Api
```

The API listens on <http://localhost:5080>.

## Tests

```bash
dotnet test
```

Integration tests start their own SQL Server container through Testcontainers, so Docker must be running.
They cover sign-up and keys, data isolation between merchants, deposits, withdrawals, same- and cross-currency
transfers, validation, paging, idempotent retries (also sent in parallel), parallel withdrawals racing for one
balance, opposite transfers that would deadlock without ordered locks, the double-entry invariants, and signed
webhook delivery with retries.

## Project structure

```text
src/
  PayLedger.Domain/          Entities and rules: Account, Transaction (double entry), Currencies, no dependencies
  PayLedger.Application/     Use cases: merchants, accounts, payments, idempotency, paging, webhook outbox
  PayLedger.Infrastructure/
    Persistence/             AppDbContext, entity configurations, migrations, row locking
    ExchangeRates/           FX rate client with resilience and caching
    Webhooks/                Background sender, HMAC signer, SSRF-safe HTTP handler
    Maintenance/             Expired idempotency key cleanup
  PayLedger.Api/             Controllers, API key auth, error mapping, rate limits, Program.cs
tests/
  PayLedger.UnitTests/       Domain rules, currency conversion, network rules, signatures
  PayLedger.IntegrationTests/ Full HTTP tests against SQL Server in Docker
docs/DECISIONS.md            Why things are built this way, and the trade-offs
Dockerfile, compose.yaml     Container image and local stack
```

## Design decisions

See [docs/DECISIONS.md](docs/DECISIONS.md) for the reasoning behind the ledger model, locking, idempotency,
webhooks, and what would change for production scale.

## License

[MIT](LICENSE)

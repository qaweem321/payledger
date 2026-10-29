# Design decisions

Short notes on why PayLedger is built the way it is, and what I would change at a larger scale.

## Money is a `long` in minor units

Amounts are integers: cents for USD, fils (3 decimals) for KWD, whole yen for JPY. Floating point can't hold
0.10 exactly, and `decimal` invites mixing scales. Each currency's number of decimals lives in one place
(`Currencies`), and conversion is the only place that does decimal maths.

Conversions round **down**. Rounding half-up would sometimes credit a fraction of a unit that was never paid in;
over many transfers that is real money created from nothing. An amount that converts to zero is refused rather
than silently swallowed.

## Double-entry ledger with settlement accounts

Every transaction writes ledger entries that add up to zero per currency. Money from outside enters through a
**settlement account** (one per merchant and currency, created at sign-up so no request ever races to create
one). That gives an invariant you can check at any time: for one merchant and currency, all balances sum to zero,
and every account's balance equals the sum of its entries. The integration tests check exactly this after
running opposite transfers in parallel.

Balances are stored on the account (not summed on every read) and each entry keeps `BalanceAfter`, so statements
need no running totals. Entries are never updated or deleted.

## Concurrency: pessimistic row locks in a fixed order

A transfer reads two balances and writes both. With plain read-then-write, two parallel withdrawals can both
see the same balance and overdraw. Options considered:

- **Optimistic (`rowversion`) + retry.** Works, but under contention most requests fail and retry.
- **Conditional update (`UPDATE ... WHERE Balance >= @x`).** Fast, but the rules move out of the domain into SQL.
- **`SELECT ... WITH (UPDLOCK, ROWLOCK)` inside the transaction (chosen).** The domain code stays plain C#, and
  waiting requests queue instead of failing. Ids are sorted before locking, so two opposite transfers lock in the
  same order and cannot deadlock. `rowversion` stays on the table as a safety net.

Anything slow happens **before** locks are taken: the exchange rate is fetched first, so a slow rate service never
holds rows locked.

Trade-off: the settlement account is a "hot row": all deposits of one merchant in one currency queue on it.
That is fine for this scale. At high volume I would shard settlement into several sub-accounts, or post settlement
entries asynchronously from the outbox.

## Idempotency in the same transaction as the payment

The `Idempotency-Key` row (unique on merchant + key) is inserted **first, inside the payment's transaction**.
A parallel request with the same key blocks on the unique index until the first one commits, then gets a
duplicate-key error and replays the stored result. If the first one fails, its key row rolls back with it, so a
retry runs again (errors are not cached; only completed payments are). The key also stores a hash of the request,
so reusing it for a different body is refused (`idempotency_key_reused`). Keys expire after 24 hours.

## Webhooks: transactional outbox

Sending an HTTP request inside a database transaction either delays the payment or loses the event if the
process dies after commit. Instead the event row is written in the same transaction as the payment, and a
`BackgroundService` sends it. Claiming a batch uses `UPDATE TOP(n) ... WITH (READPAST, UPDLOCK) ... OUTPUT`, so
several API instances can run the sender without sending the same event twice at the same moment; the claimed rows'
`NextAttemptAt` acts as a lease, so a crashed instance's batch is retried later. Delivery is at-least-once, so the
receiver de-duplicates on `PayLedger-Delivery`.

Signing follows the common `t=...,v1=HMAC(t.body)` scheme; the timestamp lets receivers reject replays.

**SSRF:** a webhook URL is a request the server makes on a merchant's behalf. The URL is checked when saved, and
the HTTP handler checks the **resolved IP at connect time** (a public name can point at `169.254.169.254` or
`10.0.0.1`), refuses redirects and ignores proxies.

## API keys, not JWT

Payment APIs are called server-to-server, where a long-lived secret key is the norm (Stripe, Adyen). Keys are
32 random bytes; only their SHA-256 hash is stored. A slow password hash would add nothing for a 256-bit random
value, and a fast hash keeps authentication to one indexed lookup per request. Rotation replaces the key at once.

## Clean architecture, but pragmatic

`Application` depends on EF Core's `DbSet` through `IAppDbContext` instead of a repository per entity: LINQ
queries stay readable, and the SQL Server specifics (lock hints, error numbers) stay in `Infrastructure`. The
domain project has no package references at all.

## Exchange rates

ExchangeRate-API's open endpoint needs no key and covers PKR, AED and SAR (the ECB-based services don't). It
updates daily, so one table per base currency is cached for an hour. The client uses the standard resilience
pipeline (retry with jitter, timeouts, circuit breaker); if the service is down, transfers between currencies fail
fast with `503 exchange_rate_unavailable` and nothing moves. Same-currency payments keep working.

## What I'd add for production

- Database-generated public ids (e.g. `txn_...`) instead of exposing sequential numbers.
- Refunds and holds (authorise, then capture), with their own state machine.
- Daily reconciliation job that checks the ledger invariants and alerts.
- Encrypting webhook secrets at rest, and live/test key modes.
- OpenTelemetry traces and metrics; Azure SQL + Container Apps deployment from CI.

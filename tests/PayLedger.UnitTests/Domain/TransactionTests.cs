using PayLedger.Domain.Common;
using PayLedger.Domain.Transactions;

namespace PayLedger.UnitTests.Domain;

public class TransactionTests
{
    private static readonly DateTimeOffset Now = TestAccounts.Now;

    [Fact]
    public void Deposit_credits_wallet_and_debits_settlement()
    {
        var settlement = TestAccounts.Settlement();
        var wallet = TestAccounts.Wallet();

        var tx = Transaction.Deposit(settlement, wallet, 5_000, "order-1", Now);

        Assert.Equal(5_000, wallet.Balance);
        Assert.Equal(-5_000, settlement.Balance);
        Assert.Equal(0, tx.Entries.Sum(e => e.Amount));
        Assert.Equal([-5_000, 5_000], tx.Entries.Select(e => e.Amount));
        Assert.Equal(5_000, tx.Entries.Single(e => e.AccountId == wallet.Id).BalanceAfter);
    }

    [Fact]
    public void Withdrawal_cannot_overdraw_a_wallet()
    {
        var settlement = TestAccounts.Settlement();
        var wallet = TestAccounts.Wallet();
        Transaction.Deposit(settlement, wallet, 1_000, null, Now);

        var error = Assert.Throws<DomainException>(() => Transaction.Withdraw(wallet, settlement, 1_001, null, Now));

        Assert.Equal("insufficient_funds", error.Code);
        Assert.Equal(1_000, wallet.Balance);
    }

    [Fact]
    public void Same_currency_transfer_moves_money_between_wallets()
    {
        var settlement = TestAccounts.Settlement();
        var source = TestAccounts.Wallet();
        var destination = TestAccounts.Wallet();
        Transaction.Deposit(settlement, source, 10_000, null, Now);

        var tx = Transaction.Transfer(source, destination, 2_500, fx: null, "rent", Now);

        Assert.Equal(7_500, source.Balance);
        Assert.Equal(2_500, destination.Balance);
        Assert.Equal(2, tx.Entries.Count);
        Assert.Null(tx.ExchangeRate);
    }

    [Fact]
    public void Cross_currency_transfer_balances_each_currency_on_its_own()
    {
        var usdSettlement = TestAccounts.Settlement("USD");
        var pkrSettlement = TestAccounts.Settlement("PKR");
        var usd = TestAccounts.Wallet("USD");
        var pkr = TestAccounts.Wallet("PKR");
        Transaction.Deposit(usdSettlement, usd, 10_000, null, Now);

        var tx = Transaction.Transfer(usd, pkr, 10_000, new FxLeg(278.45m, usdSettlement, pkrSettlement), null, Now);

        Assert.Equal(0, usd.Balance);
        Assert.Equal(2_784_500, pkr.Balance);
        Assert.Equal(2_784_500, tx.DestinationAmount);
        Assert.Equal(278.45m, tx.ExchangeRate);
        Assert.All(tx.Entries.GroupBy(e => e.Currency), g => Assert.Equal(0, g.Sum(e => e.Amount)));
        Assert.Equal(4, tx.Entries.Count);
    }

    [Fact]
    public void Cross_currency_transfer_needs_a_rate() =>
        Assert.Equal("rate_required", Assert.Throws<DomainException>(() =>
            Transaction.Transfer(TestAccounts.Wallet("USD"), TestAccounts.Wallet("EUR"), 100, null, null, Now)).Code);

    [Fact]
    public void Transfer_that_converts_to_nothing_is_rejected()
    {
        var pkr = TestAccounts.Wallet("PKR");
        var usd = TestAccounts.Wallet("USD");
        var fx = new FxLeg(0.0036m, TestAccounts.Settlement("PKR"), TestAccounts.Settlement("USD"));

        // 1 paisa is 0.0036 cents: nothing to credit, so the transfer must fail instead of losing money.
        var error = Assert.Throws<DomainException>(() => Transaction.Transfer(pkr, usd, 1, fx, null, Now));

        Assert.Equal("amount_too_small", error.Code);
    }

    [Fact]
    public void Transfer_to_the_same_account_is_rejected()
    {
        var wallet = TestAccounts.Wallet();
        Assert.Equal("same_account", Assert.Throws<DomainException>(() => Transaction.Transfer(wallet, wallet, 1, null, null, Now)).Code);
    }

    [Fact]
    public void Transfer_between_merchants_is_rejected() =>
        Assert.Equal("invalid_account", Assert.Throws<DomainException>(() =>
            Transaction.Transfer(TestAccounts.Wallet(merchantId: 1), TestAccounts.Wallet(merchantId: 2), 1, null, null, Now)).Code);

    [Fact]
    public void Transfer_from_a_settlement_account_is_rejected() =>
        Assert.Equal("invalid_account", Assert.Throws<DomainException>(() =>
            Transaction.Transfer(TestAccounts.Settlement(), TestAccounts.Wallet(), 1, null, null, Now)).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Amounts_must_be_positive(long amount) =>
        Assert.Equal("invalid_amount", Assert.Throws<DomainException>(() =>
            Transaction.Deposit(TestAccounts.Settlement(), TestAccounts.Wallet(), amount, null, Now)).Code);
}

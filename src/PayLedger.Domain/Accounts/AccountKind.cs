namespace PayLedger.Domain.Accounts;

public enum AccountKind
{
    /// <summary>A wallet that holds money for the merchant or one of its customers. Never negative.</summary>
    Wallet = 0,

    /// <summary>
    /// The merchant's view of the outside world in one currency (bank, card network, FX desk).
    /// Money entering the ledger comes from here, so it may go negative.
    /// </summary>
    Settlement = 1,
}

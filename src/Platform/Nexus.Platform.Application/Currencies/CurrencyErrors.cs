using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Currencies;

/// <summary>Caller-facing errors of the currency use cases.</summary>
public static class CurrencyErrors
{
    public static Error NotFound(string code) => Error.NotFound($"Currency '{code}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The currency has changed since the ETag sent in If-Match was obtained.");

    /// <summary>
    /// Rule ORG-R7: a currency still referenced by an active <c>Group</c> (reporting currency,
    /// which always counts — <c>org."group"</c> has no <c>is_active</c>) or an active
    /// <c>LegalEntity</c> cannot be deactivated.
    /// </summary>
    public static readonly Error InUse =
        new("ORG-R7", "This currency is still used by an active Group or Legal Entity and cannot be deactivated.");
}

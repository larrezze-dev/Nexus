using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Currencies;

/// <summary>
/// <c>org.currency</c> (domain/org.md §3). Seeded once by <c>db/seed/common</c> (<c>CHF</c>,
/// <c>EUR</c>, <c>USD</c>, <c>GBP</c>) — there is no create through the API
/// (<c>contracts/openapi/platform.yaml</c>, <c>/v1/currencies</c>: <c>GET</c> only plus
/// <c>PUT .../{code}</c> to flip <see cref="IsActive"/>). <see cref="Activate"/> and
/// <see cref="Deactivate"/> are plain state mutations with no domain event: unlike every other
/// aggregate here, <c>org.currency</c>'s natural key is a 3-letter ISO code
/// (<see cref="AggregateRoot{TId}"/>'s <c>TId</c> is <see cref="string"/>, not <see cref="Guid"/>),
/// and <see cref="IDomainEvent.AggregateId"/> requires a <see cref="Guid"/> — minting a synthetic
/// one just to satisfy the envelope is an Outbox/eventing design decision outside this track's
/// scope (flagged for Luciano/orchestrator; see step 0.4-B2's report). ORG-R7 ("never deactivate
/// a currency a <c>Group</c>/<c>LegalEntity</c> still uses") is enforced by
/// <c>UpdateCurrencyHandler</c>, not here: this aggregate has no way to see those other tables,
/// so there is no invariant for it to guard on its own.
/// </summary>
public sealed class Currency : AggregateRoot<string>
{
    private Currency(string id)
        : base(id)
    {
    }

    public IReadOnlyDictionary<string, string> CaptionI18n { get; private set; } = new Dictionary<string, string>();

    public short MinorUnits { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Idempotent: activating an already active currency changes nothing. Returns whether anything changed.</summary>
    public bool Activate()
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        return true;
    }

    /// <summary>
    /// Idempotent: deactivating an already inactive currency changes nothing. Returns whether
    /// anything changed. Callers must check ORG-R7 themselves before calling this (see the
    /// class remarks) — this method does not and cannot.
    /// </summary>
    public bool Deactivate()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        return true;
    }
}

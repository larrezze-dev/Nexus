using Nexus.BuildingBlocks.Application;

namespace Nexus.BuildingBlocks.Messaging.Inbox;

/// <summary>
/// The identity of the event being consumed in the current DI scope. Scoped. Only
/// <see cref="InboxProcessor"/> sets it (the setter is internal), right before it begins the
/// scope's unit of work, and clears it before returning; an HTTP request scope never sets it.
/// </summary>
public sealed class ConsumerIdentity
{
    /// <summary>The user the consumer's transaction runs as, or <see langword="null"/> outside a consumer.</summary>
    public Guid? UserId { get; private set; }

    internal void Set(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A consumer identity cannot be the empty UUID (BE-R1).", nameof(userId));
        }

        if (UserId is { } current && current != userId)
        {
            throw new InvalidOperationException(
                "The consumer identity of this scope is already set to a different user; use one DI scope per consumed message.");
        }

        UserId = userId;
    }

    internal void Clear() => UserId = null;
}

/// <summary>
/// The scope's <see cref="ICurrentUser"/> once <c>AddNexusInbox</c> has run: the consumed
/// event's identity when <see cref="ConsumerIdentity"/> is set (consumer scopes), otherwise
/// the host's original <see cref="ICurrentUser"/> (e.g. <c>HttpCurrentUser</c> for HTTP
/// requests), or "not authenticated" in a consumer-only host.
/// <para>
/// A consumer identity has no IdP identity (<see cref="Oid"/>, <see cref="Subject"/>) and no
/// resolved <see cref="PartyId"/>/<see cref="BusinessUnitId"/>: only <see cref="UserId"/>,
/// which is all the unit of work needs for <c>app.current_user_id</c>.
/// </para>
/// </summary>
internal sealed class ConsumerAwareCurrentUser(ConsumerIdentity consumer, ICurrentUser? inner) : ICurrentUser
{
    private bool IsConsumer => consumer.UserId is not null;

    public bool IsAuthenticated => IsConsumer || inner?.IsAuthenticated == true;

    public string? Oid => IsConsumer ? null : inner?.Oid;

    public string? Subject => IsConsumer ? null : inner?.Subject;

    public Guid? UserId => IsConsumer ? consumer.UserId : inner?.UserId;

    public Guid? PartyId => IsConsumer ? null : inner?.PartyId;

    public Guid? BusinessUnitId => IsConsumer ? null : inner?.BusinessUnitId;
}

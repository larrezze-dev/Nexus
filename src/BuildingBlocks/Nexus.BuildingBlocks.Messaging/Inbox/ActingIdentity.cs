namespace Nexus.BuildingBlocks.Messaging.Inbox;

/// <summary>
/// Whose identity a consumer's transaction runs under (conventions-events.md §7: "the step's
/// ficha says which"). It becomes <c>app.current_user_id</c> of the unit of work, i.e. the RLS
/// scope and the <c>actor_user_id</c> of every event the effect emits.
/// </summary>
public enum ActingIdentity
{
    /// <summary>A system reaction: the transaction runs as the SYSTEM user (<see cref="SystemUser.Id"/>).</summary>
    System,

    /// <summary>
    /// The action is taken on behalf of the user who caused the event: the transaction runs as
    /// the envelope's <c>actor_user_id</c>. If the event carries no actor (the header is optional
    /// in the envelope), it falls back to the SYSTEM user and logs a warning.
    /// </summary>
    OnBehalfOfActor,
}

/// <summary>Outcome of <see cref="IInboxProcessor.ProcessOnceAsync"/>.</summary>
public enum InboxResult
{
    /// <summary>The effect ran and was committed together with the inbox row.</summary>
    Processed,

    /// <summary>
    /// This consumer group had already processed the event: the effect did not run and nothing
    /// was written. The caller commits the offset as usual.
    /// </summary>
    AlreadyProcessed,
}

/// <summary>
/// The SYSTEM user: actor of jobs and automatic reactions. Seeded by
/// <c>db/seed/common/V202610241001__seed__org_common.sql</c>; the same id as
/// <c>Nexus.Platform.Domain.Users.AppUser.SystemUserId</c> (ORG-R6). Duplicated here because
/// the building blocks cannot depend on a service's domain project.
/// </summary>
public static class SystemUser
{
    public static readonly Guid Id = new("00000000-0000-7000-8000-000000000001");
}

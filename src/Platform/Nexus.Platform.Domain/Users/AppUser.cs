using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Domain.Users;

/// <summary>
/// A NEXUS application user (<c>org.app_user</c>, domain/org.md §3): the person
/// (<see cref="PartyId"/>, a <c>PERSON</c> party) who signs in through the identity provider
/// with the <c>oid</c> stored in <see cref="ExternalSubject"/> (ADR-0006).
/// </summary>
public sealed class AppUser : AggregateRoot<Guid>
{
    /// <summary>
    /// The SYSTEM user (seeded by <c>db/seed/common</c>): the actor of jobs and automatic
    /// processes. Never created, edited or deactivated through the API and never signs in
    /// (Rule ORG-R6).
    /// </summary>
    public static readonly Guid SystemUserId = new("00000000-0000-7000-8000-000000000001");

    private static readonly Error SystemUserIsImmutable =
        new("ORG-R6", "The SYSTEM user cannot be created, edited or deactivated.");

    private AppUser(Guid id)
        : base(id)
    {
    }

    public Guid PartyId { get; private set; }

    public Guid? BusinessUnitId { get; private set; }

    /// <summary>The identity provider's <c>oid</c>. <see langword="null"/> = cannot sign in.</summary>
    public string? ExternalSubject { get; private set; }

    public string? Email { get; private set; }

    /// <summary>One of <see cref="Languages.Supported"/>.</summary>
    public string PreferredLanguage { get; private set; } = Languages.Default;

    public bool IsActive { get; private set; }

    /// <summary><see langword="true"/> for the seeded SYSTEM user (ORG-R6).</summary>
    public bool IsSystem => Id == SystemUserId;

    /// <summary>
    /// Creates an active user for <paramref name="party"/> and raises <see cref="UserCreated"/>.
    /// <paramref name="externalSubject"/> is mandatory on creation (decided 2026-10-24,
    /// domain/org.md §3), although the column allows <c>NULL</c>.
    /// </summary>
    public static Result<AppUser> Create(
        Guid id,
        Party party,
        string externalSubject,
        string? email,
        string preferredLanguage,
        Guid? businessUnitId)
    {
        Guard.Against.Null(party, nameof(party));
        Guard.Against.NullOrWhiteSpace(externalSubject, nameof(externalSubject));
        Guard.Against.NullOrWhiteSpace(preferredLanguage, nameof(preferredLanguage));

        // Rule ORG-R6: SYSTEM is seeded, never created through the API (and it never has an
        // external_subject, which every created user has).
        var notSystem = EnsureNotSystem(id);
        if (notSystem.IsFailure)
        {
            return Result.Failure<AppUser>(notSystem.Error!);
        }

        // Rule ORG-R4: app_user.party_id points to a PERSON party.
        if (party.PartyType != PartyType.Person)
        {
            return Result.Failure<AppUser>(new Error("ORG-R4", "A user's party must be a PERSON."));
        }

        var user = new AppUser(id)
        {
            PartyId = party.Id,
            BusinessUnitId = businessUnitId,
            ExternalSubject = externalSubject,
            Email = email,
            PreferredLanguage = preferredLanguage,
            IsActive = true,
        };

        user.RaiseEvent(new UserCreated(
            user.Id, user.PartyId, user.BusinessUnitId, user.ExternalSubject, user.PreferredLanguage, user.IsActive));

        return Result.Success(user);
    }

    /// <summary>
    /// Replaces the editable state (PUT semantics: a <see langword="null"/>
    /// <paramref name="externalSubject"/> means "cannot sign in", not "unchanged"). Returns
    /// whether anything changed; raises <see cref="UserUpdated"/> only in that case.
    /// </summary>
    public Result<bool> Update(string? externalSubject, string? email, string preferredLanguage, Guid? businessUnitId)
    {
        Guard.Against.NullOrWhiteSpace(preferredLanguage, nameof(preferredLanguage));

        // Rule ORG-R6: the SYSTEM user is never edited.
        if (IsSystem)
        {
            return Result.Failure<bool>(SystemUserIsImmutable);
        }

        var changed =
            !string.Equals(ExternalSubject, externalSubject, StringComparison.Ordinal)
            || !string.Equals(Email, email, StringComparison.Ordinal)
            || !string.Equals(PreferredLanguage, preferredLanguage, StringComparison.Ordinal)
            || BusinessUnitId != businessUnitId;

        if (!changed)
        {
            return Result.Success(false);
        }

        ExternalSubject = externalSubject;
        Email = email;
        PreferredLanguage = preferredLanguage;
        BusinessUnitId = businessUnitId;

        RaiseEvent(new UserUpdated(Id, PartyId, BusinessUnitId, ExternalSubject, PreferredLanguage, IsActive));
        return Result.Success(true);
    }

    /// <summary>
    /// Rule ORG-R6: fails for the SYSTEM user's id. Lets a use case reject an operation on
    /// SYSTEM before doing anything else (e.g. an idempotent create that reuses its id).
    /// </summary>
    public static Result EnsureNotSystem(Guid id) =>
        id == SystemUserId ? Result.Failure(SystemUserIsImmutable) : Result.Success();

    /// <summary>
    /// Deactivates the user (no DELETE in master data). Idempotent: deactivating an inactive
    /// user changes nothing and raises nothing. Returns whether anything changed.
    /// </summary>
    public Result<bool> Deactivate()
    {
        // Rule ORG-R6: the SYSTEM user is never deactivated.
        if (IsSystem)
        {
            return Result.Failure<bool>(SystemUserIsImmutable);
        }

        if (!IsActive)
        {
            return Result.Success(false);
        }

        IsActive = false;
        RaiseEvent(new UserDeactivated(Id, PartyId, BusinessUnitId, ExternalSubject, PreferredLanguage, IsActive));
        return Result.Success(true);
    }
}

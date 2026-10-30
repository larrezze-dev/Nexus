namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Identifies the caller of the current scope (an HTTP request, a consumed event, a job run).
/// <para>
/// In HTTP services the implementation is <c>HttpCurrentUser</c> (Nexus.BuildingBlocks.Web),
/// which reads the validated JWT (ADR-0006). <c>UnitOfWork</c> depends on this abstraction to
/// set <c>app.current_user_id</c> in the RLS session of every unit of work
/// (conventions-database.md §7, BE-R1).
/// </para>
/// <para>
/// Identity has two layers: the identity-provider identity (<see cref="Oid"/>,
/// <see cref="Subject"/>), known as soon as the token is validated, and the NEXUS user
/// (<see cref="UserId"/> = <c>org.app_user.id</c>), resolved from <see cref="Oid"/> against
/// <c>org.app_user.external_subject</c> (step 0.4). A caller can be authenticated and still
/// have no <see cref="UserId"/> (an <c>oid</c> without an active <c>app_user</c>:
/// 403 <c>user_not_provisioned</c>).
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// <see langword="true"/> when the scope carries a validated identity (for HTTP: a JWT
    /// that passed issuer, audience, signature and lifetime validation).
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The <c>oid</c> claim: the stable object id of the user in the identity provider (Entra
    /// ID; in Keycloak a protocol mapper emits it, ADR-0006). Kept as the raw string, exactly
    /// as it is compared with <c>org.app_user.external_subject</c> (<c>text</c>).
    /// <see langword="null"/> when not authenticated or when the token has no <c>oid</c>.
    /// </summary>
    string? Oid { get; }

    /// <summary>
    /// The standard JWT <c>sub</c> claim. Informational (logs, diagnostics): Entra ID emits a
    /// pairwise <c>sub</c> per application, so user mapping never uses it — it uses
    /// <see cref="Oid"/>. <see langword="null"/> when not authenticated or absent.
    /// </summary>
    string? Subject { get; }

    /// <summary>
    /// The NEXUS user id (<c>org.app_user.id</c>, a UUIDv7). <see langword="null"/> while the
    /// caller is not resolved to an active <c>app_user</c>. A unit of work refuses to begin
    /// without a non-empty value (BE-R1).
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// The <c>org.party</c> (always a <c>PERSON</c>, ORG-R4) behind <see cref="UserId"/>:
    /// <c>org.app_user.party_id</c>. Resolved together with <see cref="UserId"/>, from the same
    /// row, so both are either set or <see langword="null"/>.
    /// </summary>
    Guid? PartyId { get; }

    /// <summary>
    /// The user's home business unit (<c>org.app_user.business_unit_id</c>). Optional in the
    /// data model, so it can be <see langword="null"/> even for a resolved user; always
    /// <see langword="null"/> when <see cref="UserId"/> is.
    /// </summary>
    Guid? BusinessUnitId { get; }
}

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// Database settings of a NEXUS service, bound by the host from <see cref="SectionName"/>
/// (<c>Nexus:Database</c>) and consumed by <see cref="PersistenceServiceCollectionExtensions.AddNexusPersistence"/>:
/// <code>
/// "Nexus": {
///   "Database": {
///     "ConnectionString": "Host=localhost;Port=5432;Database=nexus;Username=svc_platform",
///     "Password": "..."            // never in appsettings.json: user-secrets / env var / Key Vault
///   }
/// }
/// </code>
/// Environment variables: <c>Nexus__Database__ConnectionString</c>, <c>Nexus__Database__Password</c>.
/// The connection string must authenticate as the service's own role (<c>svc_&lt;service&gt;</c>,
/// conventions-backend.md §9); <see cref="Password"/> exists so that appsettings.json can carry
/// the non-secret part and only the secret comes from a secret store (CLAUDE.md §3).
/// </summary>
public sealed class NexusPersistenceOptions
{
    /// <summary>Configuration section the options are read from.</summary>
    public const string SectionName = "Nexus:Database";

    /// <summary>Npgsql connection string of the service role. Required on first database use.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Optional password, applied on top of <see cref="ConnectionString"/> when set (it wins
    /// over any password already in the connection string).
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Root directory of the local Avro schemas (laid out like <c>contracts/events</c>, EV-R2).
    /// Defaults to <c>contracts/events</c> under the application's base directory, where the
    /// service project copies them at build time.
    /// </summary>
    public string? EventSchemaRoot { get; set; }
}

using System.Globalization;

namespace Nexus.Platform.Application.Users;

/// <summary>
/// The optimistic-concurrency version of the User resource (BE-R3; conventions-api.md §5,
/// <c>ETag: "&lt;xmin&gt;"</c>).
/// <para>
/// A User spans two rows: <c>org.app_user</c> and its <c>org.party</c> (<c>fullName</c> is
/// <c>party.legal_name</c>). A version built from the <c>app_user</c> <c>xmin</c> alone would let
/// a PUT silently overwrite a rename made in between through the party, so the version carries
/// both <c>xmin</c> values; the ETag stays an opaque token (<c>"&lt;user xmin&gt;.&lt;party xmin&gt;"</c>).
/// </para>
/// </summary>
public readonly record struct UserVersion(uint User, uint Party)
{
    /// <summary>The token, without the ETag quotes (those are HTTP's business).</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{User}.{Party}");

    /// <summary>Parses a token produced by <see cref="ToString"/>.</summary>
    public static bool TryParse(string? token, out UserVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var separator = token.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0
            || !uint.TryParse(token.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var user)
            || !uint.TryParse(token.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var party))
        {
            return false;
        }

        version = new UserVersion(user, party);
        return true;
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Nexus.BuildingBlocks.Web.Http;

/// <summary>
/// <c>ETag</c>/<c>If-Match</c> plumbing for optimistic concurrency (conventions-api.md §5:
/// <c>GET</c> returns <c>ETag: "&lt;version&gt;"</c>; <c>PUT</c> and transitions require
/// <c>If-Match</c>: missing → 428, different → 409). The version token itself is opaque here;
/// each resource decides what it is made of (normally the row's <c>xmin</c>).
/// </summary>
public static class ETags
{
    /// <summary>Writes <c>ETag: "&lt;token&gt;"</c> on the response.</summary>
    public static void SetETag(this HttpResponse response, string token)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        response.Headers[HeaderNames.ETag] = Quote(token);
    }

    /// <summary>The strong entity tag for <paramref name="token"/>: <c>"&lt;token&gt;"</c>.</summary>
    public static string Quote(string token) => "\"" + token + "\"";

    /// <summary>
    /// Reads <c>If-Match</c>. Returns <see langword="false"/> when the header is absent (→ 428).
    /// Otherwise <paramref name="token"/> is the unquoted value of the single entity tag sent,
    /// or <see langword="null"/> when the header is present but is not exactly one strong entity
    /// tag (a list, <c>*</c>, a weak tag, garbage): such a header can never match a current
    /// version, so the caller answers 409 just like for a stale one.
    /// </summary>
    public static bool TryGetIfMatch(this HttpRequest request, out string? token)
    {
        ArgumentNullException.ThrowIfNull(request);

        token = null;
        var values = request.Headers[HeaderNames.IfMatch];
        if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        if (values.Count == 1
            && EntityTagHeaderValue.TryParse(values[0], out var tag)
            && !tag.IsWeak
            && tag.Tag.Length > 2
            && tag.Tag != "*")
        {
            token = tag.Tag.Value![1..^1];
        }

        return true;
    }
}

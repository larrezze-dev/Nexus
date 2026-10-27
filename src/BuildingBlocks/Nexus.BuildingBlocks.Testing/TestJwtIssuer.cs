using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>
/// Issues JWTs for tests, signed with an RSA key generated in memory (never a file, never a
/// real IdP, ADR-0006's "no coexistence" of concerns does not apply here - this is purely a
/// test double). Pair it with <see cref="WebAppFixture{TProgram}"/>, which makes the host under
/// test trust <see cref="SigningKey"/> instead of fetching a real OIDC discovery document.
/// </summary>
public sealed class TestJwtIssuer : IDisposable
{
    private readonly RSA _rsa;

    public TestJwtIssuer(
        string issuer = "https://test.nexus.local/",
        string audience = "api://nexus")
    {
        _rsa = RSA.Create(2048);
        Issuer = issuer;
        Audience = audience;
        SigningKey = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
    }

    /// <summary>The `iss` every token issued by <see cref="IssueToken"/> carries unless
    /// overridden.</summary>
    public string Issuer { get; }

    /// <summary>The `aud` every token issued by <see cref="IssueToken"/> carries unless
    /// overridden.</summary>
    public string Audience { get; }

    /// <summary>The RSA public/private key pair the issued tokens are signed with. A host under
    /// test must configure its <c>TokenValidationParameters.IssuerSigningKey</c> with this same
    /// key (<see cref="WebAppFixture{TProgram}"/> does it).</summary>
    public RsaSecurityKey SigningKey { get; }

    /// <summary>
    /// Issues a signed JWT. Every parameter defaults to a value that validates cleanly against
    /// a host configured with <see cref="Issuer"/>/<see cref="Audience"/>/<see cref="SigningKey"/>
    /// and a non-expired lifetime, so a test only needs to override whatever it means to break.
    /// </summary>
    /// <param name="oid">The `oid` claim (<c>org.app_user.external_subject</c>, ADR-0006).
    /// <see langword="null"/> omits the claim entirely.</param>
    /// <param name="subject">The standard `sub` claim. <see langword="null"/> omits it.</param>
    /// <param name="issuer">Overrides <see cref="Issuer"/>, e.g. to test a rejected issuer.</param>
    /// <param name="audience">Overrides <see cref="Audience"/>, e.g. to test a rejected
    /// audience.</param>
    /// <param name="lifetime">How long from now the token is valid; negative issues an already-
    /// expired token.</param>
    /// <param name="additionalClaims">Extra claims (e.g. `name`, `email`, `locale`).</param>
    public string IssueToken(
        string? oid = "00000000-0000-7000-8000-00000000a001",
        string? subject = "00000000-0000-7000-8000-00000000a001",
        string? issuer = null,
        string? audience = null,
        TimeSpan? lifetime = null,
        IEnumerable<Claim>? additionalClaims = null)
    {
        var claims = new List<Claim>();

        if (subject is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        if (oid is not null)
        {
            claims.Add(new Claim("oid", oid));
        }

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var now = DateTime.UtcNow;
        var effectiveLifetime = lifetime ?? TimeSpan.FromMinutes(5);

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: effectiveLifetime >= TimeSpan.Zero ? now : now.Add(effectiveLifetime).AddMinutes(-1),
            expires: now.Add(effectiveLifetime),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void Dispose() => _rsa.Dispose();
}

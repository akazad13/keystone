using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.IdentityService.Services;

/// <summary>
/// Owns the RSA key that signs Keystone access tokens.
/// <para>
/// Outside Development the key must come from configuration (<c>Jwt:SigningKey</c>, a PEM or
/// base64-encoded PEM) so that every replica signs with the same key. In Development a throwaway
/// key is generated at startup.
/// </para>
/// The key id (<c>kid</c>) is the RFC 7638 thumbprint of the public key, so it is identical on
/// every replica and changes only when the key itself changes.
/// </summary>
public sealed class KeyManager : IKeyManager, IDisposable
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly JsonWebKeySet _jwks;

    public KeyManager(IConfiguration configuration, IHostEnvironment environment, ILogger<KeyManager> logger)
    {
        _rsa = RSA.Create();

        var configuredKey = configuration["Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            _rsa.ImportFromPem(ToPem(configuredKey));
        }
        else if (environment.IsDevelopment())
        {
            _rsa.KeySize = 2048;
            logger.LogWarning(
                "Jwt:SigningKey is not configured. Using a temporary signing key; tokens will not survive a restart.");
        }
        else
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured outside Development. See docs/azure-deployment.md.");
        }

        var publicJwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(
            new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false)));
        var keyId = Base64UrlEncoder.Encode(publicJwk.ComputeJwkThumbprint());

        _signingKey = new RsaSecurityKey(_rsa) { KeyId = keyId };

        publicJwk.Kid = keyId;
        publicJwk.Use = JsonWebKeyUseNames.Sig;
        publicJwk.Alg = SecurityAlgorithms.RsaSha256;
        _jwks = new JsonWebKeySet();
        _jwks.Keys.Add(publicJwk);
    }

    public SecurityKey GetSigningKey() => _signingKey;

    public JsonWebKeySet GetJsonWebKeySet() => _jwks;

    public void Dispose() => _rsa.Dispose();

    // Multi-line PEM is awkward in environment variables and deployment prompts, so base64 of the PEM is accepted too.
    private static string ToPem(string value) =>
        value.Contains("-----BEGIN", StringComparison.Ordinal)
            ? value
            : Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

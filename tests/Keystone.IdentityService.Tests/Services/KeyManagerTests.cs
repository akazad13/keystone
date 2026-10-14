using Keystone.IdentityService.Services;
using Keystone.IdentityService.Tests.Fakes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Keystone.IdentityService.Tests.Services;

public sealed class KeyManagerTests
{
    [Fact]
    public void Development_without_a_configured_key_generates_a_2048_bit_key()
    {
        using var keys = TestSetup.DevelopmentKeyManager();

        Assert.Equal(2048, keys.GetSigningKey().KeySize);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_Development_a_configured_key_is_required(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new KeyManager(TestSetup.Configuration(), TestSetup.Environment(environment), NullLogger<KeyManager>.Instance));

        Assert.Contains("Jwt:SigningKey", error.Message);
    }

    [Fact]
    public void Jwks_publishes_the_public_key_for_signature_verification()
    {
        using var keys = TestSetup.DevelopmentKeyManager();

        var jwk = Assert.Single(keys.GetJsonWebKeySet().Keys);

        Assert.Equal("RSA", jwk.Kty);
        Assert.Equal("sig", jwk.Use);
        Assert.Equal(SecurityAlgorithms.RsaSha256, jwk.Alg);
        Assert.Equal(keys.GetSigningKey().KeyId, jwk.Kid);
        Assert.False(string.IsNullOrEmpty(jwk.N));
        Assert.False(string.IsNullOrEmpty(jwk.E));
    }

    [Fact]
    public void Jwks_never_contains_the_private_key()
    {
        using var keys = TestSetup.DevelopmentKeyManager();

        var jwk = Assert.Single(keys.GetJsonWebKeySet().Keys);

        Assert.Null(jwk.D);
        Assert.Null(jwk.P);
        Assert.Null(jwk.Q);
        Assert.Null(jwk.DP);
        Assert.Null(jwk.DQ);
        Assert.Null(jwk.QI);
    }

    [Fact]
    public void Key_id_is_the_RFC_7638_thumbprint_of_the_public_key()
    {
        using var keys = TestSetup.DevelopmentKeyManager();
        var jwk = keys.GetJsonWebKeySet().Keys.Single();

        Assert.Equal(Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint()), jwk.Kid);
    }

    [Fact]
    public void Replicas_configured_with_the_same_key_publish_the_same_key_id()
    {
        var pem = TestSetup.NewPrivateKeyPem();

        using var replica1 = TestSetup.KeyManagerWith(pem);
        using var replica2 = TestSetup.KeyManagerWith(pem);

        Assert.Equal(replica1.GetSigningKey().KeyId, replica2.GetSigningKey().KeyId);
    }

    [Fact]
    public void Accepts_the_key_as_base64_encoded_PEM()
    {
        var pem = TestSetup.NewPrivateKeyPem();

        using var fromPem = TestSetup.KeyManagerWith(pem);
        using var fromBase64 = TestSetup.KeyManagerWith(TestSetup.ToBase64(pem));

        Assert.Equal(fromPem.GetSigningKey().KeyId, fromBase64.GetSigningKey().KeyId);
    }

    [Fact]
    public void Different_keys_have_different_key_ids()
    {
        using var first = TestSetup.KeyManagerWith(TestSetup.NewPrivateKeyPem());
        using var second = TestSetup.KeyManagerWith(TestSetup.NewPrivateKeyPem());

        Assert.NotEqual(first.GetSigningKey().KeyId, second.GetSigningKey().KeyId);
    }

    [Fact]
    public void A_configured_key_wins_over_the_development_fallback()
    {
        var pem = TestSetup.NewPrivateKeyPem();
        using var production = TestSetup.KeyManagerWith(pem);

        using var development = new KeyManager(
            TestSetup.Configuration(("Jwt:SigningKey", pem)), TestSetup.Environment(Environments.Development), NullLogger<KeyManager>.Instance);

        Assert.Equal(production.GetSigningKey().KeyId, development.GetSigningKey().KeyId);
    }
}

using Microsoft.IdentityModel.Tokens;

namespace Keystone.IdentityService.Services;

public interface IKeyManager
{
    /// <summary>The private key used to sign Keystone access tokens.</summary>
    SecurityKey GetSigningKey();

    /// <summary>The public half of the signing key, published at /.well-known/jwks.json.</summary>
    JsonWebKeySet GetJsonWebKeySet();
}

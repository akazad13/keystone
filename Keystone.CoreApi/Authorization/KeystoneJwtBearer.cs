using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Keystone.CoreApi.Authorization;

/// <summary>
/// Validates Keystone access tokens issued by the Identity Service. Validation is offline: the
/// signing keys are fetched once from the Identity Service's JWKS and cached.
/// </summary>
public static class KeystoneJwtBearer
{
    /// <summary>Named HttpClient for the Identity Service; resolves "identity-service" through Aspire service discovery.</summary>
    public const string IdentityServiceClientName = "identity-service";

    public const string Audience = "core-api";

    public static AuthenticationBuilder AddKeystoneJwtBearer(this AuthenticationBuilder builder)
    {
        builder.AddJwtBearer(options =>
        {
            // The discovery document gives the expected issuer and the JWKS location.
            options.MetadataAddress = $"http://{IdentityServiceClientName}/.well-known/openid-configuration";
            options.RequireHttpsMetadata = false; // internal network only

            options.Audience = Audience;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "role";
            options.TokenValidationParameters.ValidTypes = ["at+jwt"];
        });

        // The metadata request must resolve "identity-service" through Aspire service discovery,
        // so it uses an HttpClient from the factory instead of the handler's own.
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpClientFactory>((options, httpClientFactory) =>
                options.Backchannel = httpClientFactory.CreateClient(IdentityServiceClientName));

        return builder;
    }
}

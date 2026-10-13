using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Keystone.IdentityService.Models;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Keystone.IdentityService.Services;

/// <summary>
/// Looks users up at the external identity provider's OIDC userinfo endpoint.
/// <para>
/// Calling userinfo with the access token is also how the token is validated: the provider
/// answers 401 for an expired, revoked or forged token. This works for both JWT and opaque
/// access tokens, so Keystone never has to know the provider's token format.
/// </para>
/// This is the only class that knows about Zitadel-specific claims.
/// </summary>
public sealed class UserService : IUserService
{
    public const string HttpClientName = "identity-provider";

    // Zitadel returns project roles as an object keyed by role name, e.g.
    // "urn:zitadel:iam:org:project:roles": { "Admin": { "<orgId>": "<orgDomain>" } }
    // The project-scoped variant is "urn:zitadel:iam:org:project:<projectId>:roles".
    private const string ZitadelRolesClaimPrefix = "urn:zitadel:iam:org:project:";
    private const string ZitadelRolesClaimSuffix = ":roles";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _discovery;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IHttpClientFactory httpClientFactory,
        IConfigurationManager<OpenIdConnectConfiguration> discovery,
        ILogger<UserService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _discovery = discovery;
        _logger = logger;
    }

    public async Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        var metadata = await _discovery.GetConfigurationAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, metadata.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogInformation("Identity provider rejected the subject token ({StatusCode})", response.StatusCode);
            return null;
        }

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var claims = document.RootElement;

        return new UserInfo
        {
            Sub = claims.GetProperty("sub").GetString()!,
            Name = GetString(claims, "name"),
            Email = GetString(claims, "email"),
            EmailVerified = claims.TryGetProperty("email_verified", out var verified) && verified.ValueKind == JsonValueKind.True,
            PreferredUsername = GetString(claims, "preferred_username"),
            Roles = GetZitadelRoles(claims)
        };
    }

    private static string? GetString(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> GetZitadelRoles(JsonElement claims)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var claim in claims.EnumerateObject())
        {
            if (claim.Name.StartsWith(ZitadelRolesClaimPrefix, StringComparison.Ordinal)
                && claim.Name.EndsWith(ZitadelRolesClaimSuffix, StringComparison.Ordinal)
                && claim.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var role in claim.Value.EnumerateObject())
                {
                    roles.Add(role.Name);
                }
            }
        }

        return [.. roles];
    }
}

using Keystone.IdentityService.Endpoints;
using Keystone.IdentityService.Services;
using Keystone.ServiceDefaults;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, resilience
builder.AddServiceDefaults();

// --- Signing key and token issuing ---
builder.Services.AddSingleton<IKeyManager, KeyManager>();
builder.Services.AddSingleton<ITokenService, TokenService>();

// --- External identity provider (Zitadel) ---
// Discovery metadata is fetched once and cached; it tells us where the userinfo endpoint is.
var authority = builder.Configuration["Oidc:Authority"]
    ?? throw new InvalidOperationException("Oidc:Authority is not configured.");

builder.Services.AddHttpClient(UserService.HttpClientName);
builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(sp =>
    new ConfigurationManager<OpenIdConnectConfiguration>(
        $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever(sp.GetRequiredService<IHttpClientFactory>().CreateClient(UserService.HttpClientName))
        {
            RequireHttps = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        }));
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// Aspire default endpoints: /health, /alive
app.MapDefaultEndpoints();

app.MapAuthEndpoints();
app.MapWellKnownEndpoints();
app.MapInternalEndpoints();

app.Run();

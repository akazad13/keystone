using System.Threading.RateLimiting;
using Keystone.Bff.Endpoints;
using Keystone.Bff.Extensions;
using Keystone.Bff.Services;
using Keystone.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// --- Service Defaults (OpenTelemetry, Health Checks, Resilience, Service Discovery) ---
builder.AddServiceDefaults();

// --- Redis: sessions, Data Protection keys, token cache, logout-token replay cache ---
// Uses the app's managed identity against Azure Managed Redis; a plain connection locally.
builder.AddRedisClientBuilder("redis").WithAzureAuthentication();

// --- Data Protection (keys shared through Redis so every replica can read every cookie) ---
builder.Services.AddDataProtection().SetApplicationName("Keystone.Bff");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IConnectionMultiplexer>((options, redis) =>
        options.XmlRepository = new RedisXmlRepository(() => redis.GetDatabase(), "keystone:bff:dataprotection-keys"));

// --- Authentication: cookie session + OpenID Connect ---
var oidc = builder.Configuration.GetSection("Oidc");
var authority = oidc["Authority"] ?? throw new InvalidOperationException("Oidc:Authority is not configured.");

builder.Services.AddSingleton<RedisTicketStore>();
builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieSessionStore>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // API calls without a session get a 401 from the cookie handler, never a redirect to the IdP.
    // Only the explicit /auth/login endpoint challenges the OpenID Connect handler.
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    // __Host- prefix: the browser only accepts it with Secure, Path=/ and no Domain.
    options.Cookie.Name = "__Host-keystone";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Lax, not Strict: the redirect back from the identity provider is a cross-site navigation,
    // and a Strict cookie would not be sent on it.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    // Discovery: everything else (endpoints, signing keys, issuer) is read from
    // {Authority}/.well-known/openid-configuration.
    options.Authority = authority;
    options.RequireHttpsMetadata = authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    options.ClientId = oidc["ClientId"];
    options.ClientSecret = oidc["ClientSecret"];

    // Authorization code flow with PKCE; the code arrives as a query parameter on a top-level GET.
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.ResponseMode = OpenIdConnectResponseMode.Query;
    options.UsePkce = true;

    options.Scope.Clear();
    foreach (var scope in oidc.GetSection("Scopes").Get<string[]>() ?? ["openid", "profile", "email"])
    {
        options.Scope.Add(scope);
    }

    // Keep id_token, access_token and refresh_token in the session (Redis), for id_token_hint at
    // logout and for the token exchange. They never reach the browser.
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;

    // Keep the JWT claim names as issued ("sub", "sid", "name") instead of the legacy
    // WS-Federation URIs. Back-channel logout matches sessions on "sid", so make sure
    // no claim action strips it.
    options.MapInboundClaims = false;
    options.ClaimActions.Remove("sid");
    options.TokenValidationParameters.NameClaimType = "name";

    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";
});

builder.Services.AddAuthorization();

// --- Token exchange (identity-provider token -> Keystone token for the Core API) ---
builder.Services.AddHttpClient(SessionTokenService.HttpClientName,
    client => client.BaseAddress = new Uri("http://identity-service"));
builder.Services.AddSingleton<ISessionTokenService, SessionTokenService>();
builder.Services.AddSingleton<LogoutTokenValidator>();

// --- Rate Limiting (one fixed window per signed-in user, per frontend) ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    foreach (var policy in new[] { "keystone-api", "keystone-admin-api" })
    {
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("sub")?.Value ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
    }
});

// --- YARP Reverse Proxy ---
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(context =>
    {
        // Only API routes carry a token; the dev-time frontend routes do not.
        if (context.Route.ClusterId != "core-api")
        {
            return;
        }

        context.AddRequestTransform(async transformContext =>
        {
            // Never forward the browser's cookies to the API.
            transformContext.ProxyRequest.Headers.Remove("Cookie");

            var token = await transformContext.HttpContext.GetTokenFromSessionAsync();
            if (!string.IsNullOrEmpty(token))
            {
                transformContext.ProxyRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        });
    });

var app = builder.Build();

// --- Middleware Pipeline ---
// Behind Azure Container Apps ingress (which terminates TLS) Aspire sets ASPNETCORE_FORWARDEDHEADERS_ENABLED,
// so X-Forwarded-Proto is honoured and redirect URIs are built as https.
if (!app.Environment.IsDevelopment())
{
    // In Azure the built frontends are in wwwroot: keystone at "/", keystone-admin at "/admin".
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// CSRF defence for API calls, checked before anything else: a cross-site page can make the browser
// send a form POST with our cookie, but it cannot add a custom header without a CORS preflight,
// which the BFF never grants.
app.Use(async (context, next) =>
{
    var request = context.Request;
    var isUnsafeApiCall = request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
        && !request.Path.Value!.EndsWith("/auth/logout", StringComparison.Ordinal);

    if (isUnsafeApiCall && request.Headers["X-CSRF"] != "1")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// --- Endpoints ---
app.MapAuthEndpoints("/api/keystone", appRoot: "/");
app.MapAuthEndpoints("/api/keystone-admin", appRoot: "/admin/");
app.MapBackChannelLogout();
app.MapReverseProxy();
app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    // Client-side routes: unknown paths load the matching SPA's index.html.
    app.MapFallbackToFile("/admin/{**path}", "admin/index.html");
    app.MapFallbackToFile("index.html");
}

app.Run();

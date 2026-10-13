var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Azure hosting
// ---------------------------------------------------------------------------
// Every project below is deployed as an Azure Container App inside this environment
// when you run `aspire deploy`. Locally (`aspire run`) this line has no effect.
builder.AddAzureContainerAppEnvironment("keystone-env");

// ---------------------------------------------------------------------------
// Infrastructure
// ---------------------------------------------------------------------------
// Locally both run as containers. In Azure they become Azure SQL Database and
// Azure Managed Redis, and the apps reach them with their managed identity (no passwords).
var sql = builder.AddAzureSqlServer("sql")
    .RunAsContainer(container => container.WithDataVolume());
var keystoneDb = sql.AddDatabase("keystonedb");

var redis = builder.AddAzureManagedRedis("redis")
    .RunAsContainer();

// ---------------------------------------------------------------------------
// External identity provider (Zitadel)
// ---------------------------------------------------------------------------
// The IdP always lives outside the AppHost: a local Zitadel started from zitadel/docker-compose.yml,
// or Zitadel Cloud in Azure. See docs/local-development.md and docs/azure-deployment.md.
// Values come from configuration ("Parameters" section, e.g. user secrets) or are prompted for.
// Only local runs fall back to the local Zitadel, so a deployment can never silently use localhost.
var oidcAuthority = builder.ExecutionContext.IsRunMode
    ? builder.AddParameter("oidc-authority", builder.Configuration["Parameters:oidc-authority"] ?? "http://localhost:8080")
    : builder.AddParameter("oidc-authority");
var oidcClientId = builder.AddParameter("oidc-client-id");
var oidcClientSecret = builder.AddParameter("oidc-client-secret", secret: true);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
var identityService = builder.AddProject<Projects.Keystone_IdentityService>("identity-service")
    .WithEnvironment("Oidc__Authority", oidcAuthority);

if (builder.ExecutionContext.IsPublishMode)
{
    // In Azure every replica must sign with the same key, so it is supplied as a secret.
    // Locally the Identity Service generates a throwaway key at startup.
    var signingKey = builder.AddParameter("identity-signing-key", secret: true);
    identityService.WithEnvironment("Jwt__SigningKey", signingKey);
}

var coreApi = builder.AddProject<Projects.Keystone_CoreApi>("core-api")
    .WithReference(keystoneDb).WaitFor(keystoneDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(identityService).WaitFor(identityService);

// The BFF is the only public entry point. The browser talks to nothing else.
var bff = builder.AddProject<Projects.Keystone_Bff>("bff", launchProfileName: "https")
    .WithExternalHttpEndpoints()
    .WithReference(redis).WaitFor(redis)
    .WithReference(coreApi)
    .WithReference(identityService)
    .WithEnvironment("Oidc__Authority", oidcAuthority)
    .WithEnvironment("Oidc__ClientId", oidcClientId)
    .WithEnvironment("Oidc__ClientSecret", oidcClientSecret);

// ---------------------------------------------------------------------------
// Frontends
// ---------------------------------------------------------------------------
// Locally: Vite dev servers, reached through the BFF (YARP proxies "/" and "/admin").
// In Azure: built to static files and copied into the BFF container's wwwroot,
// so the SPAs and the API share one origin and the auth cookie never crosses sites.
var keystone = builder.AddViteApp("keystone", "../keystone");
var keystoneAdmin = builder.AddViteApp("keystone-admin", "../keystone-admin");

if (builder.ExecutionContext.IsRunMode)
{
    bff.WithReference(keystone).WithReference(keystoneAdmin);
}

bff.PublishWithContainerFiles(keystone, "./wwwroot");
bff.PublishWithContainerFiles(keystoneAdmin, "./wwwroot/admin");

builder.Build().Run();

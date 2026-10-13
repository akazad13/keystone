using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Keystone.CoreApi.Authorization;
using Keystone.CoreApi.Data;
using Keystone.CoreApi.Endpoints;
using Keystone.CoreApi.Services;
using Keystone.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, resilience
builder.AddServiceDefaults();

// EF Core with SQL Server. Locally a container with a password; in Azure, Azure SQL with the
// app's managed identity (the connection string Aspire injects uses Active Directory Default).
builder.AddSqlServerDbContext<AppDbContext>("keystonedb");

// Redis distributed cache (managed identity in Azure, plain connection locally)
builder.AddRedisClientBuilder("redis")
    .WithAzureAuthentication()
    .WithDistributedCache(options => options.InstanceName = "keystone:core-api:");

// JWT Bearer authentication: validates Keystone access tokens issued by the Identity Service.
// Validation is offline: the signing keys are fetched once from the Identity Service's JWKS and cached.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // The discovery document gives the expected issuer and the JWKS location.
        options.MetadataAddress = "http://identity-service/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false; // internal network only

        options.Audience = "core-api";
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "role";
        options.TokenValidationParameters.ValidTypes = ["at+jwt"];
    });

// The metadata request must resolve "identity-service" through Aspire service discovery,
// so it uses an HttpClient from the factory instead of the handler's own.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IHttpClientFactory>((options, httpClientFactory) =>
        options.Backchannel = httpClientFactory.CreateClient("identity-service"));

// Authorization policies "Admin" and "User"
builder.Services.AddAuthorizationBuilder().AddKeystonePolicies();

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Application services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply pending EF Core migrations at startup. Simple and fine for this project: EF Core takes a
// database lock, so concurrent replicas do not race. Larger systems run migrations as a deploy step.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();

// Provision/refresh the caller's Users row. Before UseAuthorization, so 403 calls still count as "seen".
app.UseUserProvisioning();

app.UseAuthorization();

// Map endpoints
app.MapProjectEndpoints();
app.MapWhoAmIEndpoints();
app.MapAdminEndpoints();
app.MapDefaultEndpoints();

app.Run();

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Keystone.IdentityService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keystone.IdentityService.Tests.Fakes;

/// <summary>Configuration, environments and keys shaped like the Identity Service's real ones.</summary>
public static class TestSetup
{
    public const string Issuer = "https://identity.keystone.internal";

    /// <summary>The settings from the service's appsettings.json, plus any overrides.</summary>
    public static IConfiguration Configuration(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audiences:0"] = "core-api",
            ["Jwt:DefaultRole"] = "User",
            ["Jwt:LifetimeMinutes"] = "15"
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static IHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    public static string NewPrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>The format the deployment docs produce: base64 of the PEM, for single-line secrets.</summary>
    public static string ToBase64(string pem) => Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));

    /// <summary>A key manager as in Development: a throwaway key generated at startup.</summary>
    public static KeyManager DevelopmentKeyManager() =>
        new(Configuration(), Environment(Environments.Development), NullLogger<KeyManager>.Instance);

    public static KeyManager KeyManagerWith(string signingKey) =>
        new(Configuration(("Jwt:SigningKey", signingKey)), Environment(Environments.Production), NullLogger<KeyManager>.Instance);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Keystone.IdentityService";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>Answers every request with a canned response and records what was sent.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public string ResponseBody { get; set; } = "{}";

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        });
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

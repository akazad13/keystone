using Keystone.Bff.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Keystone.Bff.Tests.Middleware;

public sealed class CsrfProtectionTests
{
    private static async Task<(int Status, bool ReachedApp)> Send(string method, string path, string? csrfHeader)
    {
        var reached = false;
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseCsrfHeaderCheck();
        app.Run(context =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (csrfHeader is not null)
        {
            context.Request.Headers[CsrfProtection.HeaderName] = csrfHeader;
        }

        await app.Build()(context);
        return (context.Response.StatusCode, reached);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Rejects_unsafe_api_calls_without_the_header(string method)
    {
        var (status, reached) = await Send(method, "/api/keystone/projects", csrfHeader: null);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.False(reached);
    }

    [Fact]
    public async Task Rejects_a_wrong_header_value()
    {
        var (status, _) = await Send("POST", "/api/keystone/projects", csrfHeader: "0");

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task Allows_unsafe_api_calls_with_the_header()
    {
        var (_, reached) = await Send("POST", "/api/keystone/projects", csrfHeader: "1");

        Assert.True(reached);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Safe_methods_need_no_header(string method)
    {
        var (_, reached) = await Send(method, "/api/keystone/projects", csrfHeader: null);

        Assert.True(reached);
    }

    [Theory]
    [InlineData("/api/keystone/auth/logout")]
    [InlineData("/api/keystone-admin/auth/logout")]
    public async Task Logout_form_posts_are_exempt(string path)
    {
        var (_, reached) = await Send("POST", path, csrfHeader: null);

        Assert.True(reached);
    }

    [Theory]
    [InlineData("/bff/backchannel-logout")] // server-to-server from the identity provider, no browser involved
    [InlineData("/signin-oidc")]
    public async Task Non_api_paths_are_not_checked(string path)
    {
        var (_, reached) = await Send("POST", path, csrfHeader: null);

        Assert.True(reached);
    }
}

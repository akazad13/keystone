using Keystone.Bff.Extensions;

namespace Keystone.Bff.Tests.Extensions;

/// <summary>returnUrl after login must stay on this site, or /auth/login becomes an open redirect.</summary>
public sealed class HttpContextExtensionsTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/projects")]
    [InlineData("/admin/users?page=2")]
    public void Local_paths_are_allowed(string url) => Assert.True(HttpContextExtensions.IsLocalUrl(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("projects")]
    [InlineData("javascript:alert(1)")]
    public void Anything_else_is_rejected(string? url) => Assert.False(HttpContextExtensions.IsLocalUrl(url));
}

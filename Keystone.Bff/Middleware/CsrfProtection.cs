namespace Keystone.Bff.Middleware;

/// <summary>
/// CSRF defence for API calls: every state-changing request under <c>/api</c> must carry
/// <c>X-CSRF: 1</c>. A cross-site page can make the browser send a form POST with our cookie, but it
/// cannot add a custom header without a CORS preflight, which the BFF never grants.
/// <para>
/// Logout is exempt because it is a plain HTML form post (a top-level navigation, so the browser
/// follows the redirect to the identity provider). Forcing someone to sign out is harmless.
/// </para>
/// </summary>
public static class CsrfProtection
{
    public const string HeaderName = "X-CSRF";

    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (RequiresHeader(context.Request) && context.Request.Headers[HeaderName] != "1")
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next();
        });

    private static bool RequiresHeader(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
        && !request.Path.Value!.EndsWith("/auth/logout", StringComparison.Ordinal);
}

using System.Net.Http.Headers;
using Keystone.Bff.Extensions;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Keystone.Bff.Proxy;

/// <summary>
/// Turns a browser request into a Core API request: the session cookie stays at the BFF and the
/// session's Keystone access token goes along as a Bearer token instead.
/// </summary>
public static class CoreApiTokenTransform
{
    /// <summary>The YARP cluster of the Core API (see ReverseProxy:Clusters in appsettings.json).</summary>
    public const string ClusterId = "core-api";

    /// <summary>Registers the transform on API routes only; the dev-time frontend routes carry no token.</summary>
    public static void Apply(TransformBuilderContext context)
    {
        if (context.Route.ClusterId == ClusterId)
        {
            context.AddRequestTransform(AttachSessionTokenAsync);
        }
    }

    public static async ValueTask AttachSessionTokenAsync(RequestTransformContext transformContext)
    {
        var headers = transformContext.ProxyRequest.Headers;

        // Never forward the browser's cookies to the API.
        headers.Remove("Cookie");

        // Only the BFF decides which token the API sees: drop whatever Authorization header the browser sent.
        headers.Authorization = null;

        var token = await transformContext.HttpContext.GetTokenFromSessionAsync();
        if (!string.IsNullOrEmpty(token))
        {
            headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}

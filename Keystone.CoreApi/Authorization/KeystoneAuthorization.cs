using Microsoft.AspNetCore.Authorization;
using Keystone.CoreApi.Services;

namespace Keystone.CoreApi.Authorization;

/// <summary>Who may call what, and the per-request user bookkeeping that goes with it.</summary>
public static class KeystoneAuthorization
{
    /// <summary>
    /// "Admin" requires the Admin role; "User" accepts any Keystone user (Admin or User).
    /// The roles come from Zitadel: project role -> Identity Service -> "role" claim in the Keystone token.
    /// </summary>
    public static AuthorizationBuilder AddKeystonePolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy("Admin", policy => policy.RequireRole("Admin"))
            .AddPolicy("User", policy => policy.RequireRole("Admin", "User"));

    /// <summary>
    /// Every authenticated call provisions the caller's Users row (first call) and refreshes it,
    /// so the row exists and LastSeenAt is current whichever endpoint the user hits. Register it
    /// between UseAuthentication and UseAuthorization so that calls ending in 403 still count as
    /// the user being seen.
    /// </summary>
    public static IApplicationBuilder UseUserProvisioning(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                await context.RequestServices.GetRequiredService<ICurrentUserService>().GetOrCreateAsync(context.RequestAborted);
            }

            await next();
        });
}

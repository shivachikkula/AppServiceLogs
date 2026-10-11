using AppInsightsLogs.Api.Options;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Adds browser security headers to every response. The Content-Security-Policy allows scripts, workers and fonts
/// only from this site, and network/frame access only to this site and the Azure AD B2C sign-in host (MSAL loads
/// metadata and renews tokens there). Styles allow 'unsafe-inline' because Angular and Monaco inject style elements.
/// </summary>
public static class SecurityHeaders
{
    public static string BuildContentSecurityPolicy(AzureAdB2COptions b2c)
    {
        var signIn = b2c.IsConfigured ? " https://" + b2c.AuthorityHost : string.Empty;
        return string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "worker-src 'self'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self' data:",
            $"connect-src 'self'{signIn}",
            $"frame-src{(signIn.Length > 0 ? signIn : " 'none'")}",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "object-src 'none'");
    }

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, AzureAdB2COptions b2c)
    {
        var csp = BuildContentSecurityPolicy(b2c);
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = csp;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    // API responses (logs, queries, audit) must never be cached by the browser or a proxy.
                    headers.CacheControl = "no-store";
                }
                return Task.CompletedTask;
            });
            await next();
        });
    }
}

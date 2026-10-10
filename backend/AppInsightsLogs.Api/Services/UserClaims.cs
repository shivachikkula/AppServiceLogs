using System.Security.Claims;

namespace AppInsightsLogs.Api.Services;

public static class UserClaims
{
    // Azure AD B2C user flows emit "emails" (one claim per address); custom policies and other IdPs use "email".
    private static readonly string[] EmailClaimTypes =
        ["email", "emails", ClaimTypes.Email, "signInNames.emailAddress", "preferred_username"];

    /// <summary>Returns the signed-in user's email address (trimmed, lower-case), or null if the token has none.</summary>
    public static string? GetEmail(ClaimsPrincipal user)
    {
        foreach (var type in EmailClaimTypes)
        {
            var value = user.FindAll(type).Select(c => c.Value).FirstOrDefault(v => v.Contains('@'));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().ToLowerInvariant();
            }
        }

        return null;
    }

    public static string? GetName(ClaimsPrincipal user) =>
        user.FindFirst("name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value;
}

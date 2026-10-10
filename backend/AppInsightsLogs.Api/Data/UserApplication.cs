namespace AppInsightsLogs.Api.Data;

/// <summary>
/// Grants a user (identified by the email address from their Azure AD B2C sign-in) access to one application.
/// <see cref="AppKey"/> is the name of the Key Vault secret holding the application's Application Insights
/// connection string.
/// </summary>
public sealed class UserApplication
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string ApplicationName { get; set; }
    public required string AppKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

namespace AppInsightsLogs.Api.Options;

public sealed class AdministrationOptions
{
    public const string SectionName = "Administration";

    /// <summary>Email addresses (B2C sign-in) allowed to use the admin page: grant access, rename and check applications, read the audit log.</summary>
    public string[] AdminEmails { get; set; } = [];

    public bool IsAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email) && AdminEmails.Any(a => string.Equals(a.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}

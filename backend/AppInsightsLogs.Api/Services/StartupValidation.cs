namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Checks required settings when the app starts. Outside Development, any error stops the app, so a broken
/// deployment fails immediately (and App Service keeps the previous slot) instead of failing on the first request.
/// </summary>
public static class StartupValidation
{
    public sealed record Result(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    public static Result Check(IConfiguration configuration, IHostEnvironment environment)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("LogViewerDb")))
        {
            errors.Add("ConnectionStrings:LogViewerDb is not set.");
        }

        RequireHttpsUri(configuration["KeyVault:VaultUri"], "KeyVault:VaultUri", required: true, errors);
        RequireHttpsUri(configuration["ApplicationInsights:QueryEndpoint"], "ApplicationInsights:QueryEndpoint", required: false, errors);

        foreach (var key in new[] { "ApplicationInsights:Credential", "KeyVault:Credential" })
        {
            try
            {
                CredentialFactory.Normalize(configuration[key]);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{key}: {ex.Message}");
            }
        }

        var openAiEndpoint = configuration["AzureOpenAI:Endpoint"];
        var openAiDeployment = configuration["AzureOpenAI:Deployment"];
        if (string.IsNullOrWhiteSpace(openAiEndpoint) != string.IsNullOrWhiteSpace(openAiDeployment))
        {
            errors.Add("AzureOpenAI: set both Endpoint and Deployment, or neither.");
        }
        RequireHttpsUri(openAiEndpoint, "AzureOpenAI:Endpoint", required: false, errors);

        if (configuration.GetSection("Administration:AdminEmails").Get<string[]>() is not { Length: > 0 })
        {
            warnings.Add("Administration:AdminEmails is empty: nobody can open the Admin page.");
        }

        if (!environment.IsDevelopment())
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (origins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("Cors:AllowedOrigins contains a localhost origin outside Development.");
            }
        }

        return new Result(errors, warnings);
    }

    private static void RequireHttpsUri(string? value, string key, bool required, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{key} is not set.");
            }
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{key} must be an absolute https:// URL (got '{value}').");
        }
    }
}

namespace AppInsightsLogs.Api.Options;

/// <summary>Azure AD B2C settings used to validate access tokens and to configure the Angular app's sign-in.</summary>
public sealed class AzureAdB2COptions
{
    public const string SectionName = "AzureAdB2C";

    /// <summary>B2C login host, e.g. https://contoso.b2clogin.com (or a custom domain).</summary>
    public string? Instance { get; set; }

    /// <summary>B2C tenant domain, e.g. contoso.onmicrosoft.com.</summary>
    public string? Domain { get; set; }

    /// <summary>Sign-up/sign-in user flow (or custom policy), e.g. B2C_1_signupsignin.</summary>
    public string? SignUpSignInPolicyId { get; set; }

    /// <summary>Client id of the Angular (SPA) app registration.</summary>
    public string? SpaClientId { get; set; }

    /// <summary>
    /// Client id of the API app registration: the audience of access tokens. Defaults to <see cref="SpaClientId"/>
    /// when one app registration is used for both the SPA and the API.
    /// </summary>
    public string? ApiClientId { get; set; }

    /// <summary>
    /// Scopes the SPA requests for the API, e.g. https://contoso.onmicrosoft.com/logviewer-api/Logs.Read.
    /// Defaults to the client id itself, which B2C accepts when the SPA and API share an app registration.
    /// </summary>
    public string[] ApiScopes { get; set; } = [];

    /// <summary>Optional scope name (e.g. Logs.Read) that must be present in the token's scp claim.</summary>
    public string? RequiredScope { get; set; }

    /// <summary>
    /// Development only: when B2C is not configured, every request is treated as signed in with this email.
    /// Ignored outside the Development environment.
    /// </summary>
    public string? DevelopmentUserEmail { get; set; }

    public string EffectiveApiClientId => string.IsNullOrWhiteSpace(ApiClientId) ? SpaClientId ?? string.Empty : ApiClientId;

    /// <summary>
    /// Audiences accepted in access tokens: the API and SPA client ids, plus, for each configured API scope, its
    /// App ID URI (e.g. https://contoso.onmicrosoft.com/logviewer-api for .../logviewer-api/Logs.Read) and any client
    /// id the URI contains (e.g. api://{client-id}/access_as_user). B2C puts the API's client id in "aud".
    /// </summary>
    public IReadOnlyList<string> ValidAudiences =>
        new[] { ApiClientId, SpaClientId }
            .Concat(ApiScopes.SelectMany(AudiencesFromScope))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> AudiencesFromScope(string scope)
    {
        var appIdUri = scope.Contains('/', StringComparison.Ordinal) ? scope[..scope.LastIndexOf('/')] : scope;
        yield return appIdUri;
        foreach (var part in appIdUri.Split(['/', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(part, out _))
            {
                yield return part;
            }
        }
    }

    public IReadOnlyList<string> EffectiveScopes => ApiScopes.Length > 0 ? ApiScopes : [EffectiveApiClientId];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Instance) &&
        !string.IsNullOrWhiteSpace(Domain) &&
        !string.IsNullOrWhiteSpace(SignUpSignInPolicyId) &&
        !string.IsNullOrWhiteSpace(SpaClientId);

    /// <summary>https://{instance}/{domain}/{policy} — the authority MSAL.js signs in against.</summary>
    public string Authority => $"{Instance!.TrimEnd('/')}/{Domain}/{SignUpSignInPolicyId}";

    /// <summary>https://{instance}/{domain}/{policy}/v2.0/ — where the API reads the user flow's OpenID metadata.</summary>
    public string MetadataAuthority => Authority + "/v2.0/";

    public string AuthorityHost => new Uri(Instance!).Host;
}

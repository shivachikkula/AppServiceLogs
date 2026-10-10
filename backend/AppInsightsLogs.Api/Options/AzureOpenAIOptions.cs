namespace AppInsightsLogs.Api.Options;

/// <summary>
/// Optional Azure OpenAI deployment used to write KQL from a plain-English description. The feature is hidden in
/// the UI unless Endpoint and Deployment are set.
/// </summary>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>e.g. https://my-openai.openai.azure.com/</summary>
    public string? Endpoint { get; set; }

    /// <summary>Name of the chat model deployment, e.g. gpt-4o-mini.</summary>
    public string? Deployment { get; set; }

    /// <summary>Optional key. When empty, the API's Azure identity is used (needs "Cognitive Services OpenAI User").</summary>
    public string? ApiKey { get; set; }

    public string ApiVersion { get; set; } = "2024-10-21";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Deployment);
}

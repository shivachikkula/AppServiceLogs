using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AppInsightsLogs.Api.Models;
using AppInsightsLogs.Api.Options;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Writes or fixes KQL from a plain-English request using an Azure OpenAI chat deployment. Only the request, the
/// current query/error and the table schema are sent to the model — never telemetry data.
/// </summary>
public sealed class KqlAssistant(
    HttpClient httpClient,
    TokenCredential credential,
    IOptions<AzureOpenAIOptions> options,
    ILogger<KqlAssistant> logger)
{
    public const int MaxPromptLength = 2_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => options.Value.IsConfigured;

    public async Task<GeneratedQuery> GenerateAsync(GenerateQueryRequest request, KqlSchema schema, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            throw new AppInsightsQueryException(HttpStatusCode.NotFound, "The AI query assistant is not configured (AzureOpenAI:Endpoint and Deployment).", "AssistantDisabled");
        }

        var prompt = request.Prompt?.Trim() ?? string.Empty;
        var fixing = !string.IsNullOrWhiteSpace(request.Error) && !string.IsNullOrWhiteSpace(request.CurrentQuery);
        if (prompt.Length == 0 && !fixing)
        {
            throw new AppInsightsQueryException(HttpStatusCode.BadRequest, "Describe the query you want.", "PromptRequired");
        }
        if (prompt.Length > MaxPromptLength)
        {
            throw new AppInsightsQueryException(HttpStatusCode.BadRequest, $"The description is longer than {MaxPromptLength} characters.", "PromptTooLong");
        }

        var messages = new object[]
        {
            new { role = "system", content = BuildSystemPrompt(schema) },
            new { role = "user", content = BuildUserMessage(prompt, request.CurrentQuery, request.Error) },
        };

        var endpoint = settings.Endpoint!.TrimEnd('/');
        var url = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(settings.Deployment!)}/chat/completions?api-version={Uri.EscapeDataString(settings.ApiVersion)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                messages,
                temperature = 0,
                response_format = new { type = "json_object" },
            }),
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            httpRequest.Headers.Add("api-key", settings.ApiKey);
        }
        else
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellationToken);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Azure OpenAI returned {StatusCode}: {Body}", (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
            throw new AppInsightsQueryException(
                response.StatusCode == HttpStatusCode.TooManyRequests ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway,
                response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "The AI assistant is busy. Try again in a few seconds."
                    : $"The AI assistant failed ({(int)response.StatusCode}).",
                "AssistantFailed");
        }

        var result = ParseResult(body);
        var blocked = KqlGuard.Check(result.Query);
        return blocked is null
            ? result
            : result with { Explanation = $"{result.Explanation}\n\nNote: this query will be rejected when run: {blocked}" };
    }

    internal static GeneratedQuery ParseResult(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            var parsed = JsonSerializer.Deserialize<GeneratedQuery>(content, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Query))
            {
                throw new JsonException("No query in the response.");
            }
            return new GeneratedQuery(parsed.Query.Trim(), parsed.Explanation?.Trim() ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new AppInsightsQueryException(HttpStatusCode.BadGateway, "The AI assistant returned an unexpected response. Try rephrasing.", "AssistantFailed");
        }
    }

    internal static string BuildSystemPrompt(KqlSchema schema) =>
        $$"""
        You write Kusto Query Language (KQL) queries for Azure Application Insights (the classic Application Insights
        schema, queried through the Application Insights API for a single application).

        Tables and columns:
        {{QueryService.DescribeSchema(schema)}}
        Rules:
        - Use only the tables and columns listed above. Column names are case-sensitive.
        - Never use cross-resource or external functions: app(), workspace(), resource(), cluster(), database(),
          externaldata, or evaluate plugins that call other systems.
        - Always filter time with a timestamp condition such as `| where timestamp > ago(1h)`; default to the last 24 hours.
        - Limit large results with `take`, `top` or `summarize`. For trends, use `summarize ... by bin(timestamp, ...)`
          and add `| render timechart` only if the user asks for a chart.
        - `duration` is in milliseconds; `success` is a boolean; `severityLevel` is 0 (Verbose) to 4 (Critical);
          custom properties are in the dynamic column customDimensions (use tostring(customDimensions["Key"])).
        - Prefer readable queries: one operator per line, starting lines with `|`.

        Respond with a JSON object only: {"query": "<the KQL>", "explanation": "<one or two sentences, plain English>"}
        """;

    internal static string BuildUserMessage(string prompt, string? currentQuery, string? error)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(currentQuery))
        {
            builder.Append("Current query:\n```kql\n").Append(Truncate(currentQuery, KqlGuard.MaxQueryLength)).Append("\n```\n\n");
        }
        if (!string.IsNullOrWhiteSpace(error))
        {
            builder.Append("Running it failed with this error:\n").Append(Truncate(error, 2_000)).Append("\n\nFix the query.");
            if (prompt.Length > 0)
            {
                builder.Append(" Also: ");
            }
        }
        builder.Append(prompt);
        return builder.ToString();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

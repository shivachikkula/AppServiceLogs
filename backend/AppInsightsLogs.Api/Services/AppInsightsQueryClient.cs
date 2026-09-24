using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AppInsightsLogs.Api.Options;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Thin client for the Application Insights query API
/// (<c>POST {endpoint}/v1/apps/{applicationId}/query</c>) authenticated with Microsoft Entra ID.
/// </summary>
public sealed class AppInsightsQueryClient(
    HttpClient httpClient,
    TokenCredential credential,
    IOptions<AppInsightsOptions> options,
    ILogger<AppInsightsQueryClient> logger)
{
    private readonly AppInsightsOptions _options = options.Value;

    public string? ApplicationId => ConnectionStringParser.ResolveApplicationId(_options.ConnectionString, _options.ApplicationId);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApplicationId);

    public async Task<QueryTable> QueryAsync(string query, TimeSpan timespan, CancellationToken cancellationToken)
    {
        var applicationId = ApplicationId ?? throw new AppInsightsQueryException(
            HttpStatusCode.ServiceUnavailable,
            "Application Insights is not configured. Set ApplicationInsights:ConnectionString (it must contain an ApplicationId segment) or ApplicationInsights:ApplicationId.",
            "NotConfigured");

        var endpoint = _options.QueryEndpoint.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/v1/apps/{Uri.EscapeDataString(applicationId)}/query")
        {
            Content = JsonContent.Create(new { query, timespan = System.Xml.XmlConvert.ToString(timespan) }),
        };

        AccessToken token;
        try
        {
            token = await credential.GetTokenAsync(new TokenRequestContext([$"{endpoint}/.default"]), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to acquire a Microsoft Entra ID token for Application Insights");
            throw new AppInsightsQueryException(
                HttpStatusCode.Unauthorized,
                "Could not acquire an Azure access token for Application Insights. " + FlattenMessages(ex) +
                " | Fixes: sign in with 'az login --tenant <tenant-id>' (or in Visual Studio: Tools > Options > Azure Service Authentication) " +
                "using an account that has 'Monitoring Reader' on the resource, set ApplicationInsights:TenantId to the resource's tenant, " +
                "or configure a service principal (TenantId/ClientId/ClientSecret).",
                "AuthenticationFailed");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await ParseAsync(stream, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (message, code) = ReadError(document);
            logger.LogWarning("Application Insights query failed with {StatusCode}: {Message}", (int)response.StatusCode, message);
            throw new AppInsightsQueryException(response.StatusCode, Describe(response.StatusCode, message), code);
        }

        if (document is null ||
            !document.RootElement.TryGetProperty("tables", out var tables) ||
            tables.GetArrayLength() == 0)
        {
            return new QueryTable([], []);
        }

        return QueryTable.FromJson(tables[0]);
    }

    /// <summary>Joins the distinct messages of an exception and its inner exceptions into one line.</summary>
    public static string FlattenMessages(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(FlattenMessages));
                break;
            }

            var message = string.Join(' ', current.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (message.Length > 0 && !messages.Any(m => m.Contains(message, StringComparison.Ordinal)))
            {
                messages.Add(message);
            }
        }

        return string.Join(" -> ", messages);
    }

    private static async Task<JsonDocument?> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Message, string? Code) ReadError(JsonDocument? document)
    {
        if (document is not null && document.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
            var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (error.TryGetProperty("innererror", out var inner) && inner.TryGetProperty("message", out var im))
            {
                message = $"{message} {im.GetString()}".Trim();
            }
            return (message ?? "Unknown error", code);
        }

        return ("Unknown error", null);
    }

    private static string Describe(HttpStatusCode status, string message) => status switch
    {
        HttpStatusCode.Unauthorized => $"Application Insights rejected the access token: {message}",
        HttpStatusCode.Forbidden => $"The identity does not have read access to this Application Insights resource. Grant it the 'Monitoring Reader' (or 'Reader') role. {message}",
        HttpStatusCode.NotFound => $"Application Insights resource not found. Check the ApplicationId. {message}",
        _ => message,
    };
}

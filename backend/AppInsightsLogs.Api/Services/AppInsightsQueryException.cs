using System.Net;

namespace AppInsightsLogs.Api.Services;

/// <summary>Raised when Application Insights rejects a query or cannot be reached.</summary>
public sealed class AppInsightsQueryException(HttpStatusCode statusCode, string message, string? code = null)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
}

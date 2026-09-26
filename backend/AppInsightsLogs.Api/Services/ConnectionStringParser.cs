namespace AppInsightsLogs.Api.Services;

public static class ConnectionStringParser
{
    /// <summary>Parses "Key1=Value1;Key2=Value2" into a case-insensitive dictionary.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return result;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            result[part[..separator].Trim()] = part[(separator + 1)..].Trim();
        }

        return result;
    }

    /// <summary>
    /// Resolves the Application Insights application id, preferring an explicit value over
    /// the ApplicationId segment of the connection string.
    /// </summary>
    public static string? ResolveApplicationId(string? connectionString, string? explicitApplicationId)
    {
        if (!string.IsNullOrWhiteSpace(explicitApplicationId))
        {
            return explicitApplicationId.Trim();
        }

        return Parse(connectionString).TryGetValue("ApplicationId", out var appId) && !string.IsNullOrWhiteSpace(appId)
            ? appId
            : null;
    }
}

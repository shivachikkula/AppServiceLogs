using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AppInsightsLogs.Api.Services;

/// <summary>
/// Checks user-written KQL before it is sent to Application Insights.
/// <para>
/// The API's identity can typically read every application in the catalog, so a query must not reach outside the
/// application the user selected: cross-resource functions (<c>app()</c>, <c>workspace()</c>, <c>resource()</c>...)
/// and plugins that call out to other systems are rejected. Comments and string literals are ignored when checking,
/// so <c>where message has "app("</c> is still allowed.
/// </para>
/// </summary>
public static partial class KqlGuard
{
    public const int MaxQueryLength = 20_000;

    private static readonly string[] BlockedFunctions =
        ["app", "workspace", "resource", "cluster", "database", "adx", "arg", "externaldata", "external_table"];

    private static readonly string[] BlockedPlugins =
    [
        "http_request", "http_request_post", "python", "r", "sql_request", "cosmosdb_sql_request",
        "mysql_request", "postgresql_request", "azure_digital_twins_query_request",
    ];

    /// <summary>Returns null when the query may run, otherwise the reason it was rejected.</summary>
    public static string? Check(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "The query is empty.";
        }

        if (query.Length > MaxQueryLength)
        {
            return $"The query is longer than {MaxQueryLength} characters.";
        }

        var code = StripCommentsAndStrings(query);

        if (code.TrimStart().StartsWith('.'))
        {
            return "Management commands (starting with '.') are not supported; write a query instead.";
        }

        foreach (Match match in FunctionCall().Matches(code))
        {
            var name = match.Groups["name"].Value;
            if (BlockedFunctions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return $"'{name}()' is not allowed: queries can only read the selected application.";
            }
        }

        foreach (Match match in EvaluatePlugin().Matches(code))
        {
            var plugin = match.Groups["plugin"].Value;
            if (BlockedPlugins.Contains(plugin, StringComparer.OrdinalIgnoreCase))
            {
                return $"The '{plugin}' plugin is not allowed.";
            }
        }

        return null;
    }

    public static void EnsureAllowed(string? query)
    {
        var reason = Check(query);
        if (reason is not null)
        {
            throw new AppInsightsQueryException(HttpStatusCode.BadRequest, reason, "QueryNotAllowed");
        }
    }

    /// <summary>
    /// Replaces comments and string literals ('…', "…", @'…', h"…", ```…```) with spaces, and unwraps
    /// bracket-quoted identifiers (['app']) so they are checked like plain identifiers.
    /// </summary>
    internal static string StripCommentsAndStrings(string query)
    {
        var output = new StringBuilder(query.Length);
        var i = 0;
        while (i < query.Length)
        {
            var c = query[i];

            // Line comment
            if (c == '/' && i + 1 < query.Length && query[i + 1] == '/')
            {
                while (i < query.Length && query[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            // Multi-line string literal ```...```
            if (c == '`' && string.CompareOrdinal(query, i, "```", 0, 3) == 0)
            {
                var end = query.IndexOf("```", i + 3, StringComparison.Ordinal);
                i = end < 0 ? query.Length : end + 3;
                output.Append(' ');
                continue;
            }

            // Bracket-quoted identifier ['name'] or ["name"]: keep the name.
            if (c == '[' && i + 1 < query.Length && query[i + 1] is '\'' or '"')
            {
                var quote = query[i + 1];
                var end = query.IndexOf(quote, i + 2);
                if (end > 0 && end + 1 < query.Length && query[end + 1] == ']')
                {
                    output.Append(' ').Append(query, i + 2, end - i - 2).Append(' ');
                    i = end + 2;
                    continue;
                }
            }

            // String literal, optionally prefixed with @ (verbatim) or h/H (obfuscated).
            var start = i;
            var verbatim = false;
            if ((c is '@' or 'h' or 'H') && i + 1 < query.Length && query[i + 1] is '\'' or '"'
                && (i == 0 || !IsIdentifierChar(query[i - 1])))
            {
                verbatim = c == '@';
                i++;
                c = query[i];
            }

            if (c is '\'' or '"')
            {
                var quote = c;
                i++;
                while (i < query.Length)
                {
                    if (!verbatim && query[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    if (query[i] == quote)
                    {
                        // In verbatim strings a doubled quote is an escaped quote.
                        if (verbatim && i + 1 < query.Length && query[i + 1] == quote)
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                output.Append(' ');
                continue;
            }

            i = start;
            output.Append(query[i]);
            i++;
        }

        return output.ToString();
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    [GeneratedRegex(@"(?<![\w.])(?<name>[A-Za-z_][\w]*)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionCall();

    [GeneratedRegex(@"\bevaluate\b(?:\s+\w+\s*=\s*\S+)*\s+(?<plugin>\w+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EvaluatePlugin();
}

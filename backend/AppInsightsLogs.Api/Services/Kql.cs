using System.Globalization;
using System.Text;

namespace AppInsightsLogs.Api.Services;

/// <summary>Helpers for safely embedding user input into KQL queries.</summary>
public static class Kql
{
    /// <summary>Returns a double-quoted KQL string literal with backslashes, quotes and control characters escaped.</summary>
    public static string String(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append(@"\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append(@"\n"); break;
                case '\r': builder.Append(@"\r"); break;
                case '\t': builder.Append(@"\t"); break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append(' ');
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Returns a KQL datetime literal in UTC.</summary>
    public static string DateTime(DateTimeOffset value) =>
        "datetime(" + value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) + ")";

    /// <summary>Returns a KQL timespan literal expressed in whole minutes.</summary>
    public static string Minutes(int minutes) => minutes.ToString(CultureInfo.InvariantCulture) + "m";
}

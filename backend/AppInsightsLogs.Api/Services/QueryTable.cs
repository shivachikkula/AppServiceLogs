using System.Globalization;
using System.Text.Json;

namespace AppInsightsLogs.Api.Services;

/// <summary>A single result table returned by the Application Insights query API.</summary>
public sealed class QueryTable
{
    private readonly Dictionary<string, int> _columnIndex;

    public QueryTable(IReadOnlyList<string> columns, IReadOnlyList<JsonElement[]> rows)
    {
        Columns = columns;
        Rows = rows.Select(r => new QueryRow(this, r)).ToList();
        _columnIndex = columns
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<QueryRow> Rows { get; }

    internal int IndexOf(string column) => _columnIndex.TryGetValue(column, out var index) ? index : -1;

    public static QueryTable FromJson(JsonElement table)
    {
        var columns = table.GetProperty("columns").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString() ?? string.Empty)
            .ToList();
        var rows = table.GetProperty("rows").EnumerateArray()
            .Select(r => r.EnumerateArray().Select(v => v.Clone()).ToArray())
            .ToList();
        return new QueryTable(columns, rows);
    }
}

public sealed class QueryRow(QueryTable table, JsonElement[] values)
{
    private JsonElement? Get(string column)
    {
        var index = table.IndexOf(column);
        if (index < 0 || index >= values.Length)
        {
            return null;
        }

        var value = values[index];
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : value;
    }

    public string? GetString(string column)
    {
        var value = Get(column);
        return value?.ValueKind switch
        {
            null => null,
            JsonValueKind.String => value.Value.GetString(),
            _ => value.Value.GetRawText(),
        };
    }

    public int? GetInt(string column)
    {
        var value = Get(column);
        return value?.ValueKind switch
        {
            JsonValueKind.Number => (int)value.Value.GetDouble(),
            JsonValueKind.String when int.TryParse(value.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    public long? GetLong(string column)
    {
        var value = Get(column);
        return value?.ValueKind switch
        {
            JsonValueKind.Number => (long)value.Value.GetDouble(),
            JsonValueKind.String when long.TryParse(value.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            _ => null,
        };
    }

    public double? GetDouble(string column)
    {
        var value = Get(column);
        return value?.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public DateTimeOffset? GetDateTime(string column)
    {
        var text = GetString(column);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : null;
    }

    /// <summary>Returns a dynamic column as a JSON element, parsing it if the API returned it as a string.</summary>
    public JsonElement? GetDynamic(string column)
    {
        var value = Get(column);
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind != JsonValueKind.String)
        {
            return value;
        }

        var text = value.Value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return value;
        }
    }
}

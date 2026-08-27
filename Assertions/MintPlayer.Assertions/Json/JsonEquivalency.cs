using System.Text.Json;

namespace MintPlayer.Assertions.Json;

/// <summary>
/// Deep structural comparison of two <see cref="JsonElement"/> trees. Objects compare
/// property-order-insensitively, arrays order-sensitively, numbers by numeric value
/// (<c>1.0</c> equals <c>1.00</c>; raw text is the fallback when a number does not fit a
/// <see cref="decimal"/>), strings, booleans and nulls by value. Every difference is reported
/// with its JSON path (e.g. <c>$.items[2].name: expected "a", found "b"</c>). Operates on
/// <see cref="JsonElement"/> only, so it is AOT- and trimming-safe.
/// </summary>
internal static class JsonEquivalency
{
    private const int MaxRenderedLength = 100;

    /// <summary>Returns all differences between <paramref name="actual"/> and <paramref name="expected"/>; empty when equivalent.</summary>
    public static List<string> FindDifferences(JsonElement actual, JsonElement expected)
    {
        var differences = new List<string>();
        Compare(actual, expected, "$", differences);
        return differences;
    }

    /// <summary>Describes a value kind for failure messages (e.g. "a JSON object", "JSON null").</summary>
    public static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "a JSON object",
        JsonValueKind.Array => "a JSON array",
        JsonValueKind.String => "a JSON string",
        JsonValueKind.Number => "a JSON number",
        JsonValueKind.True or JsonValueKind.False => "a JSON boolean",
        JsonValueKind.Null => "JSON null",
        _ => "an undefined JSON value",
    };

    private static void Compare(JsonElement actual, JsonElement expected, string path, List<string> differences)
    {
        // True and False are distinct value kinds but the same JSON type.
        if (IsBoolean(expected.ValueKind) && IsBoolean(actual.ValueKind))
        {
            if (expected.ValueKind != actual.ValueKind)
                differences.Add($"{path}: expected {Render(expected)}, found {Render(actual)}");
            return;
        }

        if (expected.ValueKind != actual.ValueKind)
        {
            differences.Add($"{path}: expected {Render(expected)}, found {Render(actual)}");
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObjects(actual, expected, path, differences);
                break;
            case JsonValueKind.Array:
                CompareArrays(actual, expected, path, differences);
                break;
            case JsonValueKind.String:
                if (!string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal))
                    differences.Add($"{path}: expected {Render(expected)}, found {Render(actual)}");
                break;
            case JsonValueKind.Number:
                CompareNumbers(actual, expected, path, differences);
                break;
            // Null == Null and Undefined == Undefined: nothing to compare.
        }
    }

    private static void CompareObjects(JsonElement actual, JsonElement expected, string path, List<string> differences)
    {
        foreach (var expectedProperty in expected.EnumerateObject())
        {
            if (actual.TryGetProperty(expectedProperty.Name, out var actualValue))
                Compare(actualValue, expectedProperty.Value, AppendProperty(path, expectedProperty.Name), differences);
            else
                differences.Add($"{AppendProperty(path, expectedProperty.Name)}: missing property (expected {Render(expectedProperty.Value)})");
        }

        foreach (var actualProperty in actual.EnumerateObject())
        {
            if (!expected.TryGetProperty(actualProperty.Name, out _))
                differences.Add($"{AppendProperty(path, actualProperty.Name)}: extra property (found {Render(actualProperty.Value)})");
        }
    }

    private static void CompareArrays(JsonElement actual, JsonElement expected, string path, List<string> differences)
    {
        var actualLength = actual.GetArrayLength();
        var expectedLength = expected.GetArrayLength();
        if (actualLength != expectedLength)
            differences.Add($"{path}: expected array of length {expectedLength}, found length {actualLength}");

        // Compare the common prefix element-wise so a length mismatch still yields useful detail.
        // No 'using': a using-local is read-only, so MoveNext would mutate a defensive copy.
        var actualItems = actual.EnumerateArray();
        var expectedItems = expected.EnumerateArray();
        var index = 0;
        while (actualItems.MoveNext() && expectedItems.MoveNext())
        {
            Compare(actualItems.Current, expectedItems.Current, $"{path}[{index}]", differences);
            index++;
        }
    }

    private static void CompareNumbers(JsonElement actual, JsonElement expected, string path, List<string> differences)
    {
        // Decimal comparison ignores scale, so 1.0 == 1.00. Numbers outside decimal's range
        // fall back to raw-text comparison.
        if (actual.TryGetDecimal(out var actualValue) && expected.TryGetDecimal(out var expectedValue))
        {
            if (actualValue != expectedValue)
                differences.Add($"{path}: expected {Render(expected)}, found {Render(actual)}");
        }
        else if (!string.Equals(actual.GetRawText(), expected.GetRawText(), StringComparison.Ordinal))
        {
            differences.Add($"{path}: expected {Render(expected)}, found {Render(actual)}");
        }
    }

    private static string AppendProperty(string path, string name) =>
        IsIdentifierLike(name) ? $"{path}.{name}" : $"{path}['{name.Replace("'", "\\'")}']";

    private static bool IsIdentifierLike(string name) =>
        name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string Render(JsonElement element)
    {
        var raw = element.GetRawText();
        return raw.Length > MaxRenderedLength ? raw[..MaxRenderedLength] + "…" : raw;
    }

    private static bool IsBoolean(JsonValueKind kind) => kind is JsonValueKind.True or JsonValueKind.False;
}

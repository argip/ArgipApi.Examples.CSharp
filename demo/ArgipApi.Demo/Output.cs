using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArgipApi.Demo;

/// <summary>Console output helpers: indented JSON and aligned text tables.</summary>
public static class Output
{
    // Relaxed escaping keeps non-ASCII letters (e.g. Polish product names) readable instead of \u0105 escapes.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Json(object? value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>
    /// Prints the result of a write. For an account in the testing phase the API adds a <c>Message</c> (validated, not saved):
    /// without <c>--json</c> it also gets its own line; with <c>--json</c> the JSON already contains it.
    /// </summary>
    public static void WriteResult(object? value, string? message, bool json)
    {
        Json(value);
        if (!json && !string.IsNullOrEmpty(message)) Console.WriteLine(message);
    }

    /// <summary>Prints rows as a table: column width = longest value, two spaces between columns, numbers right-aligned.</summary>
    public static void Table<T>(IEnumerable<T> rows, params (string Header, Func<T, object?> Value)[] columns)
    {
        var list = rows.ToList();
        var cells = list.Select(x => columns.Select(y => Format(y.Value(x))).ToArray()).ToList();
        var widths = columns.Select((x, i) => Math.Max(x.Header.Length, cells.Select(y => y[i].Length).DefaultIfEmpty(0).Max())).ToArray();
        // A column counts as numeric when its first value is a number.
        var numeric = columns.Select(x => list.Count > 0 && IsNumber(x.Value(list[0]))).ToArray();

        Console.WriteLine(string.Join("  ", columns.Select((x, i) => Align(x.Header, widths[i], numeric[i]))).TrimEnd());
        foreach (var row in cells)
            Console.WriteLine(string.Join("  ", row.Select((x, i) => Align(x, widths[i], numeric[i]))).TrimEnd());
    }

    private static string Align(string text, int width, bool right) => right ? text.PadLeft(width) : text.PadRight(width);

    private static bool IsNumber(object? value) => value is int or long or decimal or double or float;

    // Invariant culture: the same output on every machine, whatever its regional settings.
    private static string Format(object? value) => value switch
    {
        null => "",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}

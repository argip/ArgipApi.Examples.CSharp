using System.Globalization;

namespace ArgipApi.Demo;

/// <summary>A mistake in how the demo was called (not an API error). Reported with exit code 1.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Splits the command line into positional arguments, flags (<c>--main</c>) and options with a value (<c>--page 2</c>).
/// Small on purpose: the demo is about the API, not about argument parsing.
/// </summary>
public sealed class Arguments
{
    private static readonly HashSet<string> FlagNames = ["main", "all-states"];

    private static readonly HashSet<string> OptionNames =
    [
        "page", "size", "filter", "index", "name", "ean", "description", "postal-code", "street", "building",
        "city", "country", "phone", "person", "company", "email", "address", "date", "number", "comments",
    ];

    private readonly Dictionary<string, string> _options = [];
    private readonly HashSet<string> _flags = [];

    public List<string> Positional { get; } = [];

    public static Arguments Parse(IReadOnlyList<string> args)
    {
        var result = new Arguments();
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                result.Positional.Add(args[i]);
                continue;
            }

            var name = args[i][2..];
            if (FlagNames.Contains(name))
            {
                result._flags.Add(name);
                continue;
            }

            if (!OptionNames.Contains(name)) throw new UsageException($"Unknown option --{name}.");
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--")) throw new UsageException($"Option --{name} needs a value.");
            result._options[name] = args[++i];
        }

        return result;
    }

    public bool Flag(string name) => _flags.Contains(name);

    public string? Option(string name) => _options.GetValueOrDefault(name);

    public string RequiredOption(string name) => Option(name) ?? throw new UsageException($"Missing required option --{name}.");

    public int RequiredIntOption(string name) => ParseInt(RequiredOption(name), $"--{name}");

    public int Option(string name, int fallback) =>
        Option(name) is { } text ? ParseInt(text, $"--{name}") : fallback;

    /// <summary>The positional argument at <paramref name="index"/> (0 is the command itself), or null.</summary>
    public string? At(int index) => index < Positional.Count ? Positional[index] : null;

    public string Required(int index, string name) => At(index) ?? throw new UsageException($"Missing argument <{name}>.");

    public int RequiredInt(int index, string name) => ParseInt(Required(index, name), $"<{name}>");

    /// <summary>--page (from 0) and --size (1-1000), checked here so a bad value never reaches the network.</summary>
    public (int Page, int Size) Paging()
    {
        var page = Option("page", 0);
        var size = Option("size", 20);
        if (page < 0) throw new UsageException("--page must be 0 or more (pages start at 0).");
        if (size is < 1 or > 1000) throw new UsageException("--size must be between 1 and 1000.");
        return (page, size);
    }

    private static int ParseInt(string text, string what) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new UsageException($"{what} must be a whole number, got '{text}'.");
}

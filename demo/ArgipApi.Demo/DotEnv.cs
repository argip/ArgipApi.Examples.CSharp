namespace ArgipApi.Demo;

/// <summary>Minimal .env loader: KEY=VALUE lines, '#' comments. Real environment variables win.</summary>
public static class DotEnv
{
    public static void Load(string path = ".env")
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var separator = trimmed.IndexOf('=');
            if (separator <= 0) continue;
            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is null && value.Length > 0) Environment.SetEnvironmentVariable(key, value);
        }
    }
}

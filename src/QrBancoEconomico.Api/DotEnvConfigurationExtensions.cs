using Microsoft.Extensions.Configuration;

namespace QrBancoEconomico.Api;

public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddDotEnvFileIfPresent(this IConfigurationBuilder configuration, string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);
        FileInfo? envFile = null;
        while (directory is not null && envFile is null)
        {
            var example = Path.Combine(directory.FullName, ".env.example");
            if (File.Exists(example))
            {
                var candidate = new FileInfo(Path.Combine(directory.FullName, ".env"));
                if (candidate.Exists)
                {
                    envFile = candidate;
                }

                break;
            }

            directory = directory.Parent;
        }

        if (envFile is null)
        {
            return configuration;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(envFile.FullName))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim().Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
            var value = trimmed[(separator + 1)..].Trim();
            values[key] = value.Length >= 2 && value[0] == '"' && value[^1] == '"'
                ? value[1..^1]
                : value;
        }

        return configuration.AddInMemoryCollection(values);
    }
}

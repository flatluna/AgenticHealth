using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Common;

public static class AppConfiguration
{
    public static string? GetSetting(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    public static IConfiguration BuildFunctionsConfiguration(string basePath, string? environmentName = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath);

        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
        }

        builder.AddJsonFile("local.settings.json", optional: true, reloadOnChange: false);
        builder.AddEnvironmentVariables();

        var configuration = builder.Build();
        var values = configuration.GetSection("Values")
            .AsEnumerable()
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key.Replace("Values:", string.Empty), pair => pair.Value!);

        if (values.Count > 0)
        {
            var memoryConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            return new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddConfiguration(memoryConfig)
                .Build();
        }

        return configuration;
    }
}

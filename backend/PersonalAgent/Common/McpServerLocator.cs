using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Common;

/// <summary>
/// Resolves the command + arguments used to launch a locally-hosted MCP server process
/// over stdio. Reads an explicit override from configuration first; otherwise falls back
/// to the conventional solution layout (backend/PersonalAgent + mcp/&lt;ServerName&gt; side
/// by side under the repo root).
/// </summary>
public static class McpServerLocator
{
    public static (string Command, string[] Arguments) Resolve(
        IConfiguration configuration,
        string commandConfigKey,
        string argsConfigKey,
        string defaultServerProjectName)
    {
        var configuredCommand = configuration[commandConfigKey];
        var configuredArgs = configuration[argsConfigKey];

        if (!string.IsNullOrWhiteSpace(configuredCommand))
        {
            var args = string.IsNullOrWhiteSpace(configuredArgs)
                ? []
                : configuredArgs.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return (configuredCommand, args);
        }

        // Default: assume the repo layout used by this project -
        // <repoRoot>/backend/PersonalAgent/bin/<Config>/<tfm>/ (this app's output dir)
        // <repoRoot>/mcp/<defaultServerProjectName>/bin/<Config>/<tfm>/<defaultServerProjectName>.dll
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        var serverDll = Path.Combine(repoRoot, "mcp", defaultServerProjectName, "bin", "Debug", "net10.0", $"{defaultServerProjectName}.dll");
        return ("dotnet", [serverDll]);
    }
}

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

namespace PersonalAgent.Common;

/// <summary>
/// Owns a single, lazily-connected <see cref="McpClient"/> to the local Food MCP server
/// (launched as a child process over stdio) and exposes its tools as <see cref="AITool"/>
/// instances ready to hand to an <see cref="Microsoft.Agents.AI.AIAgent"/>.
/// Registered as a Singleton so the child process is started once per host lifetime.
/// </summary>
public sealed class FoodMcpClientProvider : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private McpClient? _client;

    public FoodMcpClientProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var client = await GetOrCreateClientAsync(cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return [.. tools];
    }

    private async Task<McpClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var (command, arguments) = McpServerLocator.Resolve(
                _configuration,
                commandConfigKey: "McpFoodServerCommand",
                argsConfigKey: "McpFoodServerArgs",
                defaultServerProjectName: "FoodMcpServer");

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "FoodMcpServer",
                Command = command,
                Arguments = arguments,
            });

            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            return _client;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}

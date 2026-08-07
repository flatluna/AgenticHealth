using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalAgent.Agents;
using PersonalAgent.Common;
using PersonalAgent.Data;
using PersonalAgent.Security;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false);
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: false);
        config.AddEnvironmentVariables();
    })
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddHttpClient();

        // DbContextFactory (not AddDbContext) so Singleton services can create short-lived
        // DbContexts without captive-dependency DI violations - same pattern as HumanOS.
        var connectionString = AppConfiguration.GetSetting(context.Configuration, "PersonalAgentDatabase", "Values:PersonalAgentDatabase");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContextFactory<PersonalAgentDbContext>(options =>
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
        }

        // MCP client provider (owns the FoodMcpServer child process connection).
        services.AddSingleton<FoodMcpClientProvider>();

        // Bing-grounded live nutrition lookups (Azure AI Foundry Agent Service).
        services.AddSingleton<BingFoodSearchProvider>();

        // Edamam's hosted Food MCP server - direct structured nutrition API, preferred over
        // Bing when configured since it skips the LLM-agent thread/run cycle entirely.
        services.AddSingleton<EdamamFoodSearchProvider>();

        // Bing-grounded nutrition/exercise plan research for the Objetivos page.
        services.AddSingleton<BingPlanSearchProvider>();

        // Mints ephemeral Azure OpenAI GPT Realtime tokens for the chat page's voice mode.
        services.AddSingleton<RealtimeVoiceSessionService>();

        // Resolves the single "default" person row used by this MVP (no auth yet).
        services.AddSingleton<DefaultPersonProvider>();

        // Lets DietAgent publish "still working on ingredient X" lines while a multi-item
        // meal's Bing lookups run, polled by the frontend via GET /api/agent/progress.
        services.AddSingleton<AgentProgressTracker>();

        // Holds the last meal DietAgent proposed in chat (nutrition breakdown) so the
        // frontend can render "Agregar a comida de hoy"/"Guardar en mi catálogo" buttons.
        services.AddSingleton<PendingMealTracker>();

        // Lightweight auth support for the landing page + profile flow.
        services.AddOptions<AuthenticationSettings>();

        // Agents (registered as Singletons - stateless, self-configure from IConfiguration).
        services.AddSingleton<DietAgent>();
        services.AddSingleton<ExerciseAgent>();
        services.AddSingleton<PersonalGeneralAgent>();
        services.AddSingleton<AdvisorAgent>();
        services.AddSingleton<OrchestratorAgent>();
        services.AddSingleton<GoalsAgent>();
        services.AddSingleton<FoodLabelExtractionAgent>();
    })
    .Build();

try
{
    using var scope = host.Services.CreateScope();
    var dbFactory = scope.ServiceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
    if (dbFactory is not null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Database initialization skipped: {ex.Message}");
}

host.Run();

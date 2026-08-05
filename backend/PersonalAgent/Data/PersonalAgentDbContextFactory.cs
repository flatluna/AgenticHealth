using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PersonalAgent.Data;

/// <summary>
/// Design-time factory used only by `dotnet ef` CLI commands (migrations add/database
/// update). The isolated-worker HostBuilder pattern in Program.cs isn't discoverable by
/// EF's design-time tooling, so this factory reads the connection string directly from
/// local.settings.json's "Values" section to build a DbContext for design-time use only.
/// </summary>
public sealed class PersonalAgentDbContextFactory : IDesignTimeDbContextFactory<PersonalAgentDbContext>
{
    public PersonalAgentDbContext CreateDbContext(string[] args)
    {
        var settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "local.settings.json");
        var json = File.ReadAllText(settingsPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var connectionString = doc.RootElement.GetProperty("Values").GetProperty("PersonalAgentDatabase").GetString();

        var optionsBuilder = new DbContextOptionsBuilder<PersonalAgentDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new PersonalAgentDbContext(optionsBuilder.Options);
    }
}

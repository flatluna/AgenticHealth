using Microsoft.EntityFrameworkCore;
using PersonalAgent.Data;

namespace PersonalAgent.Common;

/// <summary>
/// Resolves the single "default" person used by this MVP (no authentication/multi-user
/// support yet - every conversation is assumed to be the same person). Creates the row on
/// first use if it doesn't exist yet.
/// </summary>
public sealed class DefaultPersonProvider
{
    private const string DefaultPersonName = "Usuario";

    private readonly IDbContextFactory<PersonalAgentDbContext> _dbContextFactory;

    public DefaultPersonProvider(IDbContextFactory<PersonalAgentDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<int> GetOrCreateDefaultPersonIdAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.People.FirstOrDefaultAsync(p => p.Name == DefaultPersonName, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var person = new Person { Name = DefaultPersonName, HeightCm = 0 };
        db.People.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        return person.Id;
    }
}

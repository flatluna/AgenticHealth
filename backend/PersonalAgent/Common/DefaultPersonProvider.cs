using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using PersonalAgent.Data;

namespace PersonalAgent.Common;

/// <summary>
/// Resolves which Person row a given request's health-data operations (weight/meals/
/// exercise/goals) should use. Each signed-in account (identified by the "x-msal-user"
/// HTTP header - the same MSAL homeAccountId AuthFunctions.cs uses for login/profile) gets
/// its OWN isolated Person row, created on first use. If no identity header is present
/// (e.g. local dev without auth wired up), falls back to the legacy shared "Usuario" row
/// for backward compatibility - this should never happen for real authenticated traffic.
/// </summary>
public sealed class DefaultPersonProvider
{
    private const string DefaultPersonName = "Usuario";
    private const string MsalUserHeaderName = "x-msal-user";

    private readonly IDbContextFactory<PersonalAgentDbContext> _dbContextFactory;

    public DefaultPersonProvider(IDbContextFactory<PersonalAgentDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>Convenience overload for HTTP-triggered functions: extracts the caller's identity from the request and resolves/creates their own Person row.</summary>
    public Task<int> GetOrCreateDefaultPersonIdAsync(HttpRequestData request, CancellationToken cancellationToken = default)
    {
        var azureObjectId = request.Headers.TryGetValues(MsalUserHeaderName, out var values) ? values.FirstOrDefault() : null;
        return GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);
    }

    /// <summary>For callers (e.g. agent tool closures) that already extracted the caller's identity instead of holding the HttpRequestData directly.</summary>
    public async Task<int> GetOrCreatePersonIdForUserAsync(string? azureObjectId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(azureObjectId))
        {
            var existingByUser = await db.People.FirstOrDefaultAsync(p => p.AzureObjectId == azureObjectId, cancellationToken);
            if (existingByUser is not null)
            {
                return existingByUser.Id;
            }

            var newPerson = new Person { Name = DefaultPersonName, HeightCm = 0, AzureObjectId = azureObjectId };
            db.People.Add(newPerson);
            await db.SaveChangesAsync(cancellationToken);
            return newPerson.Id;
        }

        // Legacy/no-identity fallback (e.g. local dev without the auth header wired up).
        var existing = await db.People.FirstOrDefaultAsync(p => p.Name == DefaultPersonName && p.AzureObjectId == null, cancellationToken);
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

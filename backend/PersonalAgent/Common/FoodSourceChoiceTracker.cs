using System.Collections.Concurrent;

namespace PersonalAgent.Common;

/// <summary>
/// One or more concrete food queries DietAgent's fast-path classifier extracted from the
/// user's message, captured instead of auto-picking a lookup source, so the frontend can show
/// three buttons - "Catálogo local", "Edamam", "Internet" - and let the user choose which
/// specialized search to run. Travels back to the backend unchanged on the chosen button's
/// follow-up request (see FoodSourceSearchFunction), so no extra per-session state is needed
/// once the choice reaches the frontend - it's fully self-contained.
/// </summary>
public sealed record FoodSourceChoiceDto(
    string[] Queries,
    string[] OriginalQueries,
    string MealType,
    bool AlreadyConsumed,
    string OriginalPrompt);

/// <summary>In-memory, per-session single-slot holder for the last food-source choice DietAgent's
/// fast path offered - AgentAskFunction takes it right after each AgentAsk call completes, same
/// pull-once pattern as PendingMealTracker.</summary>
public sealed class FoodSourceChoiceTracker
{
    private readonly ConcurrentDictionary<string, FoodSourceChoiceDto> _bySession = new();

    public void Set(string sessionId, FoodSourceChoiceDto choice) => _bySession[sessionId] = choice;

    /// <summary>Returns and clears the pending choice for this session, if any.</summary>
    public FoodSourceChoiceDto? Take(string sessionId) =>
        _bySession.TryRemove(sessionId, out var choice) ? choice : null;
}

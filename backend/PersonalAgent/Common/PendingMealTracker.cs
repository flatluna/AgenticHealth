using System.Collections.Concurrent;

namespace PersonalAgent.Common;

/// <summary>
/// Full nutrition breakdown for a meal DietAgent just proposed to the user in chat (same
/// fields as the "log_meal"/"propose_meal_for_confirmation" tool), captured so the frontend
/// can render "Agregar a comida de hoy"/"Guardar en mi catálogo" buttons using the exact
/// values the model computed, instead of re-parsing free text or round-tripping the LLM.
/// </summary>
public sealed record PendingMealDto(
    string MealType,
    string Description,
    string? ServingSize,
    double? Calories,
    double? ProteinGrams,
    double? CarbsGrams,
    double? FatGrams,
    double? SaturatedFatGrams,
    double? SugarGrams,
    double? FiberGrams,
    double? SodiumMilligrams,
    double? PotassiumMilligrams,
    double? CalciumMilligrams,
    double? IronMilligrams,
    double? MagnesiumMilligrams,
    double? VitaminAMicrograms,
    string? ConsumedAtIso,
    string? SourceBreakdown,
    /// <summary>Short, generic, LLM-generated name for saving into the personal catalog (ej. "Arroz con huevo frito") - distinct from Description, which can be more specific/verbose (ej. "150g de arroz y dos huevos fritos").</summary>
    string? CatalogName = null,
    /// <summary>Short, generic, LLM-generated description for the personal catalog (ej. "Plato de arroz blanco con huevos fritos en aceite de oliva").</summary>
    string? CatalogDescription = null);

/// <summary>
/// In-memory, per-session single-slot holder for the last meal DietAgent proposed (via the
/// "propose_meal_for_confirmation" tool) - AgentAskFunction takes it right after each
/// AgentAsk call completes, so it's only ever present in the response for the exact turn
/// where the model just presented a nutrition breakdown, not later unrelated turns.
/// </summary>
public sealed class PendingMealTracker
{
    private readonly ConcurrentDictionary<string, PendingMealDto> _bySession = new();

    public void Set(string sessionId, PendingMealDto meal) => _bySession[sessionId] = meal;

    /// <summary>Returns and clears the pending meal for this session, if any.</summary>
    public PendingMealDto? Take(string sessionId) =>
        _bySession.TryRemove(sessionId, out var meal) ? meal : null;
}

using Microsoft.EntityFrameworkCore;
using PersonalAgent.Data;

namespace PersonalAgent.Common;

/// <summary>
/// Formats the user's recently logged meals into a plain-text summary for an LLM tool
/// result, so agents can look up "lo mismo que ayer"-style references instead of asking
/// the user to repeat a description. Shared between DietAgent (text chat) and
/// VoiceToolsFunction (voice mode) so both read the exact same fields the exact same way.
/// </summary>
public static class MealHistoryHelper
{
    public static async Task<string> GetRecentMealsSummaryAsync(
        IDbContextFactory<PersonalAgentDbContext> dbContextFactory,
        int personId,
        int? daysBack,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, daysBack.GetValueOrDefault(14)));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var meals = await db.MealLogs
            .Where(m => m.PersonId == personId && m.RecordedAtUtc >= cutoffUtc)
            .OrderByDescending(m => m.RecordedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (meals.Count == 0)
        {
            return "No hay comidas registradas en ese rango de fechas.";
        }

        var lines = meals.Select(m =>
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(m.RecordedAtUtc, MealTimeHelper.Central);
            var line = $"- [ID {m.Id}] {local:yyyy-MM-dd HH:mm} ({m.MealType}): {m.Description}" +
                (string.IsNullOrWhiteSpace(m.ServingSize) ? "" : $" · {m.ServingSize}") +
                $" · {m.Calories?.ToString("0") ?? "?"} kcal, {m.ProteinGrams?.ToString("0.0") ?? "?"}g prot, " +
                $"{m.CarbsGrams?.ToString("0.0") ?? "?"}g carb, {m.FatGrams?.ToString("0.0") ?? "?"}g grasa, " +
                $"{m.SodiumMilligrams?.ToString("0") ?? "?"}mg sodio, {m.PotassiumMilligrams?.ToString("0") ?? "?"}mg potasio, " +
                $"{m.CalciumMilligrams?.ToString("0") ?? "?"}mg calcio, {m.IronMilligrams?.ToString("0") ?? "?"}mg hierro, " +
                $"{m.MagnesiumMilligrams?.ToString("0") ?? "?"}mg magnesio, {m.VitaminAMicrograms?.ToString("0") ?? "?"}µg vitamina A";
            return string.IsNullOrWhiteSpace(m.SourceBreakdown) ? line : $"{line} · Fuente: {m.SourceBreakdown}";
        });

        return string.Join("\n", lines);
    }
}

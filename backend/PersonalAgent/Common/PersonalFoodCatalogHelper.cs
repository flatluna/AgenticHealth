using Microsoft.EntityFrameworkCore;
using PersonalAgent.Data;

namespace PersonalAgent.Common;

/// <summary>
/// Shared find-or-create logic for a person's personal food catalog (Data/PersonalFoodItem.cs),
/// used by the "Guardar en mi catálogo" chat button so a computed nutrition breakdown lands
/// in a reusable per-person catalog instead of being lost once the chat scrolls past it.
/// </summary>
public static class PersonalFoodCatalogHelper
{
    public static async Task<PersonalFoodItem> FindOrCreateAsync(
        PersonalAgentDbContext db,
        int personId,
        string name,
        string? description,
        string? servingSize,
        double? calories,
        double? proteinGrams,
        double? carbsGrams,
        double? fatGrams,
        double? saturatedFatGrams,
        double? sugarGrams,
        double? fiberGrams,
        double? sodiumMilligrams,
        double? potassiumMilligrams,
        double? calciumMilligrams,
        double? ironMilligrams,
        double? magnesiumMilligrams,
        double? vitaminAMicrograms,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var item = await db.PersonalFoodItems
            .FirstOrDefaultAsync(pf => pf.PersonId == personId && pf.NormalizedName == normalized, cancellationToken);

        if (item is null)
        {
            item = new PersonalFoodItem { PersonId = personId, Name = name.Trim(), NormalizedName = normalized };
            db.PersonalFoodItems.Add(item);
        }

        // Keep the catalog's nutrition data fresh with the latest confirmed values.
        item.Description = description;
        item.ServingSize = servingSize;
        item.Calories = calories;
        item.ProteinGrams = proteinGrams;
        item.CarbsGrams = carbsGrams;
        item.FatGrams = fatGrams;
        item.SaturatedFatGrams = saturatedFatGrams;
        item.SugarGrams = sugarGrams;
        item.FiberGrams = fiberGrams;
        item.SodiumMilligrams = sodiumMilligrams;
        item.PotassiumMilligrams = potassiumMilligrams;
        item.CalciumMilligrams = calciumMilligrams;
        item.IronMilligrams = ironMilligrams;
        item.MagnesiumMilligrams = magnesiumMilligrams;
        item.VitaminAMicrograms = vitaminAMicrograms;
        item.TimesLogged++;

        return item;
    }

    /// <summary>Best-match search over THIS person's own catalog by name/description text,
    /// most-logged first - used by the "Mi catálogo" search shortcuts (ej. voz: "search_personal_catalog")
    /// so a previously-saved item can be found and reused instead of re-searching the web.</summary>
    public static async Task<List<PersonalFoodItem>> SearchAsync(
        PersonalAgentDbContext db, int personId, string query, int take, CancellationToken cancellationToken)
    {
        var pattern = $"%{query.Trim()}%";
        return await db.PersonalFoodItems
            .Where(pf => pf.PersonId == personId &&
                (EF.Functions.Like(pf.Name, pattern) || (pf.Description != null && EF.Functions.Like(pf.Description, pattern))))
            .OrderByDescending(pf => pf.TimesLogged)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Best SINGLE match from this person's own catalog using word-overlap matching
    /// (see FoodCatalogMatcher) - requires every significant word in `query` to appear
    /// somewhere in the item's name/description, in any order, unlike the substring-only
    /// SearchAsync above. Used by DietAgent's fast-path lookup where a single high-confidence
    /// match (or none) is needed.</summary>
    public static async Task<PersonalFoodItem?> FindBestWordMatchAsync(
        PersonalAgentDbContext db, int personId, string query, CancellationToken cancellationToken)
    {
        var queryWords = FoodCatalogMatcher.SignificantWords(query);
        if (queryWords.Length == 0)
        {
            return null;
        }

        var candidates = await db.PersonalFoodItems
            .Where(pf => pf.PersonId == personId)
            .ToListAsync(cancellationToken);

        return FoodCatalogMatcher.PickBestMatch(candidates, queryWords, pf => $"{pf.Name} {pf.Description}", pf => pf.TimesLogged);
    }

    /// <summary>Several word-overlap candidates (not just the single best) from this person's
    /// own catalog, for callers that want to hand a short list to an LLM to decide (ej. the
    /// voice mode "search_personal_catalog" tool) instead of committing to one deterministic
    /// pick - see FoodCatalogMatcher.RankCandidates.</summary>
    public static async Task<List<PersonalFoodItem>> RankByWordOverlapAsync(
        PersonalAgentDbContext db, int personId, string query, int take, CancellationToken cancellationToken)
    {
        var queryWords = FoodCatalogMatcher.SignificantWords(query);
        if (queryWords.Length == 0)
        {
            return [];
        }

        var candidates = await db.PersonalFoodItems
            .Where(pf => pf.PersonId == personId)
            .ToListAsync(cancellationToken);

        return FoodCatalogMatcher.RankCandidates(candidates, queryWords, pf => $"{pf.Name} {pf.Description}", pf => pf.TimesLogged, take);
    }

    /// <summary>Logs an EXISTING catalog entry as a meal, scaling all nutrients by quantity -
    /// shared by the "Adicionar" REST endpoint (FoodItemsFunction) and the voice
    /// "log_personal_catalog_item" tool (VoiceToolsFunction) so both write the exact same
    /// MealLog shape. Returns null if no matching entry exists for this person.</summary>
    public static async Task<MealLog?> LogExistingAsync(
        PersonalAgentDbContext db, int personId, int id, string? mealType, string? consumedAtIso, double? quantity,
        CancellationToken cancellationToken)
    {
        var catalogItem = await db.PersonalFoodItems
            .FirstOrDefaultAsync(pf => pf.Id == id && pf.PersonId == personId, cancellationToken);
        if (catalogItem is null)
        {
            return null;
        }

        var parsedMealType = Enum.TryParse<MealType>(mealType, ignoreCase: true, out var mt) ? mt : MealType.Snack;
        var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(consumedAtIso, DateTime.UtcNow);
        var effectiveQuantity = quantity is > 0 ? quantity.Value : 1.0;
        double? Scale(double? value) => value.HasValue ? value.Value * effectiveQuantity : null;
        var quantityLabel = effectiveQuantity == 1 ? string.Empty : $" (x{effectiveQuantity:0.##} porciones)";

        catalogItem.TimesLogged++;

        var mealLog = new MealLog
        {
            PersonId = personId,
            MealType = parsedMealType,
            Description = $"{catalogItem.Name}{quantityLabel}",
            ServingSize = catalogItem.ServingSize,
            Calories = Scale(catalogItem.Calories),
            ProteinGrams = Scale(catalogItem.ProteinGrams),
            CarbsGrams = Scale(catalogItem.CarbsGrams),
            FatGrams = Scale(catalogItem.FatGrams),
            SaturatedFatGrams = Scale(catalogItem.SaturatedFatGrams),
            SugarGrams = Scale(catalogItem.SugarGrams),
            FiberGrams = Scale(catalogItem.FiberGrams),
            SodiumMilligrams = Scale(catalogItem.SodiumMilligrams),
            PotassiumMilligrams = Scale(catalogItem.PotassiumMilligrams),
            CalciumMilligrams = Scale(catalogItem.CalciumMilligrams),
            IronMilligrams = Scale(catalogItem.IronMilligrams),
            MagnesiumMilligrams = Scale(catalogItem.MagnesiumMilligrams),
            VitaminAMicrograms = Scale(catalogItem.VitaminAMicrograms),
            SourceBreakdown = $"{catalogItem.Name}{quantityLabel}: {Scale(catalogItem.Calories)?.ToString("0") ?? "?"} kcal (mi catálogo)",
            RecordedAtUtc = recordedAt,
            PersonalFoodItem = catalogItem,
        };
        db.MealLogs.Add(mealLog);

        return mealLog;
    }
}

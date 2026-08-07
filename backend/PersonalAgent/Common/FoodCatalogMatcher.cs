using System.Text.RegularExpressions;

namespace PersonalAgent.Common;

/// <summary>
/// Shared word-overlap matcher used by BOTH the personal catalog (PersonalFoodCatalogHelper)
/// and the global catalog (FoodItems) fast-path lookups in DietAgent. Replaces the old
/// exact-substring "LIKE %query%" match, which required the ENTIRE query to appear verbatim,
/// in order, inside the catalog name - so a container word ("un plato de arroz con mole" vs
/// the saved "sopa de arroz con mole"), a different word order, or any extra/missing filler
/// word broke the match, in the SAME way for both catalogs. Instead: split the query into its
/// significant words and require ALL of them to appear somewhere in the candidate's text, in
/// any order - matching how a person recognizes "is this the same food" rather than exact
/// phrasing.
/// </summary>
public static class FoodCatalogMatcher
{
    // Container/serving words (ej. "un PLATO de arroz con mole") are stopwords too, since
    // catalog entries are usually named after the food itself (ej. "sopa de arroz con mole"),
    // not the container the user happened to describe it in - without this, requiring every
    // query word to appear in the candidate would demand a literal "plato" in the catalog name.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "del", "la", "el", "los", "las", "un", "una", "unos", "unas", "con", "y", "en",
        "para", "al", "a", "the", "with", "and", "of", "in", "for",
        "plato", "platos", "vaso", "vasos", "taza", "tazas", "tazon", "tazones", "tazón", "tazónes",
        "bowl", "bowls", "plate", "plates", "cup", "cups", "glass", "glasses", "porcion",
        "porciones", "porción", "porciónes", "serving", "servings", "pieza", "piezas", "rebanada",
        "rebanadas", "slice", "slices", "unidad", "unidades", "unit", "units",
    };

    private static readonly Regex WordPattern = new(@"\p{L}+", RegexOptions.Compiled);

    /// <summary>Splits text into lowercase significant words (letters only, length > 1, not a
    /// stopword), deduplicated - quantities ("1", "150g") and fillers ("un", "de") drop out on
    /// their own, so callers no longer need a separate quantity/container-stripping step.</summary>
    public static string[] SignificantWords(string? text) =>
        WordPattern.Matches(text ?? string.Empty)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length > 1 && !StopWords.Contains(w))
            .Distinct()
            .ToArray();

    /// <summary>Picks the candidate whose text contains ALL of the query's significant words (in
    /// any order, allowing simple plural/stem variance via substring containment), preferring the
    /// closest length match (fewest extra words) and then the most-logged one to break ties.
    /// Returns null if no candidate matches every word.</summary>
    public static T? PickBestMatch<T>(
        IEnumerable<T> candidates, string[] queryWords, Func<T, string> textSelector, Func<T, int> popularitySelector)
        where T : class
    {
        if (queryWords.Length == 0)
        {
            return null;
        }

        return candidates
            .Select(c => (Item: c, Words: SignificantWords(textSelector(c))))
            .Where(c => queryWords.All(qw => c.Words.Any(cw => cw.Contains(qw) || qw.Contains(cw))))
            .OrderBy(c => Math.Abs(c.Words.Length - queryWords.Length))
            .ThenByDescending(c => popularitySelector(c.Item))
            .Select(c => c.Item)
            .FirstOrDefault();
    }

    /// <summary>Like <see cref="PickBestMatch{T}"/> but for callers that want several
    /// candidates instead of a single confident pick (ej. voice tools, which hand a short
    /// list to the Realtime model so IT can decide which one the user meant). Unlike
    /// PickBestMatch, a candidate doesn't need to contain EVERY query word - it just needs at
    /// least one overlapping word, ranked by how many words matched (most first), then by
    /// popularity.</summary>
    public static List<T> RankCandidates<T>(
        IEnumerable<T> candidates, string[] queryWords, Func<T, string> textSelector, Func<T, int> popularitySelector, int take)
        where T : class
    {
        if (queryWords.Length == 0)
        {
            return [];
        }

        return candidates
            .Select(c => (Item: c, Words: SignificantWords(textSelector(c))))
            .Select(c => (c.Item, MatchCount: queryWords.Count(qw => c.Words.Any(cw => cw.Contains(qw) || qw.Contains(cw)))))
            .Where(c => c.MatchCount > 0)
            .OrderByDescending(c => c.MatchCount)
            .ThenByDescending(c => popularitySelector(c.Item))
            .Select(c => c.Item)
            .Take(take)
            .ToList();
    }
}

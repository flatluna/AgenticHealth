namespace PersonalAgent.Data;

/// <summary>
/// A food/meal combo saved by a specific person into their own reusable catalog (ej. "mi
/// plato de arroz con dos tortillas") - scoped to their own PersonId, unlike FoodItem which
/// is shared GLOBALLY across every user (see Data/FoodItem.cs). Created when the user taps
/// "Guardar en mi catálogo" after DietAgent computes a nutrition breakdown in chat (see
/// PersonalFoodCatalogHelper). Matched per-person by <see cref="NormalizedName"/>.
/// </summary>
public sealed class PersonalFoodItem
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Lowercased/trimmed Name, used to find-or-create the same row for this person instead of duplicating it every time they save it again.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Short, generic description (LLM-generated when saved from chat, ej. "Plato de arroz blanco con huevos fritos") - helps recognize the item later without the exact serving/quantity details baked into Name.</summary>
    public string? Description { get; set; }

    public string? ServingSize { get; set; }

    public double? Calories { get; set; }

    public double? ProteinGrams { get; set; }

    public double? CarbsGrams { get; set; }

    public double? FatGrams { get; set; }

    public double? SaturatedFatGrams { get; set; }

    public double? SugarGrams { get; set; }

    public double? FiberGrams { get; set; }

    public double? SodiumMilligrams { get; set; }

    public double? PotassiumMilligrams { get; set; }

    public double? CalciumMilligrams { get; set; }

    public double? IronMilligrams { get; set; }

    public double? MagnesiumMilligrams { get; set; }

    public double? VitaminAMicrograms { get; set; }

    /// <summary>How many times this person has logged this saved item - popularity signal for ordering, same idea as FoodItem.TimesLogged.</summary>
    public int TimesLogged { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

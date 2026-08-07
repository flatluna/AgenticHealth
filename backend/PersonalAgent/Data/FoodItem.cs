namespace PersonalAgent.Data;

/// <summary>
/// A food product identified from a scanned nutrition label, shared GLOBALLY across every
/// user (not scoped to a Person) - once one user scans a product's label, everyone else
/// benefits from the already-extracted nutrition data instead of re-scanning the same
/// product. Matched by <see cref="MatchKey"/> (normalized Name+Brand) when a scanned label
/// is confirmed for saving - see FoodLabelFunction.
/// </summary>
public sealed class FoodItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }

    /// <summary>Tamaño de porción tal como aparece en la etiqueta, ej. "1 taza (240 ml)".</summary>
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

    public string? IngredientsText { get; set; }

    /// <summary>Lowercased "name|brand" used to find-or-create the same global row instead of duplicating it every time a different user scans the same product.</summary>
    public string MatchKey { get; set; } = string.Empty;

    /// <summary>How many times any user has confirmed logging this food - a rough popularity signal.</summary>
    public int TimesLogged { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

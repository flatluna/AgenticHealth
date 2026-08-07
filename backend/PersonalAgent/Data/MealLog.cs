using System.Text.Json.Serialization;

namespace PersonalAgent.Data;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MealType
{
    Breakfast,
    Lunch,
    Dinner,
    Snack,
}

/// <summary>A meal/food entry recorded for a person, with full nutritional detail.</summary>
public sealed class MealLog
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public MealType MealType { get; set; } = MealType.Snack;

    public string Description { get; set; } = string.Empty;

    /// <summary>Tamaño de porción, ej. "100 g" o "1 unidad mediana".</summary>
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

    /// <summary>
    /// Desglose legible de cómo se obtuvo el cálculo cuando la comida tiene varios
    /// componentes (ej. "Pan: 80 kcal, 3g proteína (Bing); Mantequilla: 40 kcal, 4.5g grasa
    /// (Bing)"), para que el usuario pueda ver de dónde salen los números totales.
    /// </summary>
    public string? SourceBreakdown { get; set; }

    /// <summary>Link to the global FoodItem this meal was logged from (e.g. a scanned nutrition label), if any - null for meals logged via chat/voice/manual entry.</summary>
    public int? FoodItemId { get; set; }

    public FoodItem? FoodItem { get; set; }

    /// <summary>Link to the person's own catalog entry (Data/PersonalFoodItem.cs), if this meal was logged from "Mi catálogo" instead of chat/voice/manual entry.</summary>
    public int? PersonalFoodItemId { get; set; }

    public PersonalFoodItem? PersonalFoodItem { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

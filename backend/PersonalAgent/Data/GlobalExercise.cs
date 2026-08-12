namespace PersonalAgent.Data;

/// <summary>
/// A reusable exercise template shared across all users. Similar to FoodItem, this is global
/// catalog data that can be recommended by the AI and reused by everyone without duplicating
/// the same exercise definition across user accounts.
/// </summary>
public sealed class GlobalExercise
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public int DefaultDurationMinutes { get; set; }

    public double? DefaultCaloriesBurned { get; set; }

    public string? Description { get; set; }

    public string? Category { get; set; }

    public bool IsPublic { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int TimesUsed { get; set; }
}

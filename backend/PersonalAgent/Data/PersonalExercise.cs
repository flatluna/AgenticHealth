namespace PersonalAgent.Data;

/// <summary>
/// A custom exercise created and named by a specific person - scoped to their own PersonId,
/// unlike FoodItem which is shared GLOBALLY across every user (see Data/FoodItem.cs). Created
/// once the AI has estimated calories for a free-text activity (via chat's "log_exercise" tool
/// or the "Crea tu propio ejercicio" form on the Ejercicio tab), so the same person can quickly
/// re-log the same activity later without asking the AI to re-estimate it. Matched per-person
/// by <see cref="NormalizedName"/>.
/// </summary>
public sealed class PersonalExercise
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Lowercased/trimmed Name, used to find-or-create the same row for this person instead of duplicating it every time they log the same activity again.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Reference duration (minutes) from the last estimate/log - lets a re-log with a different duration scale CaloriesBurned proportionally.</summary>
    public int DurationMinutes { get; set; }

    public double? CaloriesBurned { get; set; }

    /// <summary>How many times this person has logged this custom exercise - popularity signal for ordering, same idea as FoodItem.TimesLogged.</summary>
    public int TimesLogged { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

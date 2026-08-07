using System.Text.Json.Serialization;

namespace PersonalAgent.Data;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivityLevel
{
    Sedentary,
    Light,
    Moderate,
    Active,
    VeryActive,
}

/// <summary>
/// A person/client tracked by the agent. Height is fixed per person; current weight is
/// derived from the latest WeightLog entry (kept here too as a quick-access snapshot).
/// </summary>
public sealed class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double HeightCm { get; set; }

    /// <summary>Age in years, used by GoalsAgent to tailor exercise intensity/recommendations.</summary>
    public int? Age { get; set; }

    /// <summary>Snapshot of the most recent known weight, in kilograms.</summary>
    public double? CurrentWeightKg { get; set; }

    /// <summary>Snapshot of the most recently reported activity level (used by GoalsAgent).</summary>
    public ActivityLevel? ActivityLevel { get; set; }

    /// <summary>
    /// Links this health-data record to the authenticated account (AppUser.AzureObjectId,
    /// the MSAL homeAccountId) that owns it - null only for the legacy shared "Usuario"
    /// row created before per-user isolation existed. Every NEW Person is created with this
    /// set so each signed-in account gets its own isolated weight/meal/exercise/goal data.
    /// </summary>
    public string? AzureObjectId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<WeightLog> WeightLogs { get; set; } = [];

    public List<MealLog> MealLogs { get; set; } = [];

    public List<ExerciseLog> ExerciseLogs { get; set; } = [];

    public List<PersonalExercise> PersonalExercises { get; set; } = [];

    public List<PersonalFoodItem> PersonalFoodItems { get; set; } = [];

    public List<Goal> Goals { get; set; } = [];

    public List<GoalPlan> GoalPlans { get; set; } = [];
}

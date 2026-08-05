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

    /// <summary>Snapshot of the most recent known weight, in kilograms.</summary>
    public double? CurrentWeightKg { get; set; }

    /// <summary>Snapshot of the most recently reported activity level (used by GoalsAgent).</summary>
    public ActivityLevel? ActivityLevel { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<WeightLog> WeightLogs { get; set; } = [];

    public List<MealLog> MealLogs { get; set; } = [];

    public List<ExerciseLog> ExerciseLogs { get; set; } = [];

    public List<Goal> Goals { get; set; } = [];

    public List<GoalPlan> GoalPlans { get; set; } = [];
}

using System.Text.Json.Serialization;

namespace PersonalAgent.Data;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GoalType
{
    Weight,
    Exercise,
    Nutrition,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GoalStatus
{
    Active,
    Achieved,
    Abandoned,
}

/// <summary>
/// A target the person is working towards - e.g. reach a target weight, exercise a certain
/// number of minutes per week, or hit a daily calorie target.
/// </summary>
public sealed class Goal
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public GoalType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Numeric target value - meaning depends on Type (kg for Weight, minutes/week for Exercise, kcal/day for Nutrition).</summary>
    public double? TargetValue { get; set; }

    public DateTime? TargetDateUtc { get; set; }

    public GoalStatus Status { get; set; } = GoalStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

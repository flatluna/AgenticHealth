namespace PersonalAgent.Data;

/// <summary>
/// A single day's "did I follow the plan today?" check-in against a GoalPlan: whether the
/// person followed the nutrition/exercise recommendations that day, how many steps they
/// walked, and optional free-text notes. One row per (GoalPlanId, CheckInDate) - lets the
/// Objetivos page show a simple daily-followable log/streak instead of just the raw plan.
/// </summary>
public sealed class GoalPlanCheckIn
{
    public int Id { get; set; }

    public int GoalPlanId { get; set; }

    public GoalPlan? GoalPlan { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public DateOnly CheckInDate { get; set; }

    public int? StepsWalked { get; set; }

    public int? WaterMl { get; set; }

    public bool FollowedNutrition { get; set; }

    public bool FollowedExercise { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

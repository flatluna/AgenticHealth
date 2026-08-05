namespace PersonalAgent.Data;

/// <summary>
/// A stored snapshot of one "generar plan" run from the Objetivos page: the inputs the
/// user gave GoalsAgent (weight/height/activity/goals text) plus the full structured JSON
/// recommendation it produced (research-grounded via Bing when configured). Kept so the
/// Objetivos page can show the last generated plan again on reload instead of losing it.
/// </summary>
public sealed class GoalPlan
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public double WeightKg { get; set; }

    public double HeightCm { get; set; }

    public double Bmi { get; set; }

    public ActivityLevel ActivityLevel { get; set; }

    public string GoalsText { get; set; } = string.Empty;

    /// <summary>Raw JSON text returned by GoalsAgent - the frontend parses/renders this.</summary>
    public string RecommendationJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<GoalPlanCheckIn> CheckIns { get; set; } = [];
}

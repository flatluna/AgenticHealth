namespace PersonalAgent.Data;

/// <summary>An exercise/activity entry recorded for a person.</summary>
public sealed class ExerciseLog
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public string Description { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }

    public double? CaloriesBurned { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

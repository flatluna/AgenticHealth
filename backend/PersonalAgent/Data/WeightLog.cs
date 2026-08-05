namespace PersonalAgent.Data;

/// <summary>A single weight measurement recorded for a person.</summary>
public sealed class WeightLog
{
    public int Id { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public double WeightKg { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

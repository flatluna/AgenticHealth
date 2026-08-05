namespace PersonalAgent.Data;

public sealed class UserProfile
{
    public int Id { get; set; }

    public int AppUserId { get; set; }

    public string? Bio { get; set; }

    public string? Goal { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? PreferredFocus { get; set; }

    public string? Timezone { get; set; }

    public bool WantsWellnessTips { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public AppUser? AppUser { get; set; }
}

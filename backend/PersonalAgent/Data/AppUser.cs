namespace PersonalAgent.Data;

public sealed class AppUser
{
    public int Id { get; set; }

    public string AzureObjectId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PreferredLanguage { get; set; } = "en";

    public string SubscriptionStatus { get; set; } = "active";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public int? ProfileId { get; set; }

    public UserProfile? Profile { get; set; }
}

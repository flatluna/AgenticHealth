using System.Globalization;

namespace PersonalAgent.Common;

/// <summary>
/// Shared "consumed at" ISO-string parsing for meal logging, used by both DietAgent (text
/// chat) and VoiceToolsFunction (voice mode) so both write the same RecordedAtUtc for the
/// same input instead of drifting apart.
/// </summary>
public static class MealTimeHelper
{
    private static readonly TimeZoneInfo CentralTimeZone = ResolveCentralTimeZone();

    /// <summary>The Central timezone the app treats as "local", used for both parsing and display.</summary>
    public static TimeZoneInfo Central => CentralTimeZone;

    // Naive (no-offset) ISO strings from the LLM represent Central local time (matching the
    // "[Fecha y hora actual: ...]" context it's given) - must be explicitly converted from
    // Central, NOT passed through DateTime.ToUniversalTime(), which assumes the value is
    // already in the SERVER's local timezone (UTC on Azure Linux, silently a no-op there,
    // but WRONG on a Windows dev machine in a different timezone).
    public static DateTime ParseCentralOrUtcToUtc(string? iso, DateTime fallbackUtc)
    {
        if (string.IsNullOrWhiteSpace(iso) ||
            !DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return fallbackUtc;
        }

        return parsed.Kind switch
        {
            DateTimeKind.Utc => parsed,
            DateTimeKind.Local => parsed.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), CentralTimeZone),
        };
    }

    private static TimeZoneInfo ResolveCentralTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        }
    }
}

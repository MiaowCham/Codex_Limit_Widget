using System.Text;
using System.Text.Json;
using CodexLimitWidget.Core.Resources;

namespace CodexLimitWidget.Core;

public sealed record RateLimitWindow(int? UsedPercent, int? WindowDurationMins, long? ResetsAt)
{
    public static RateLimitWindow Empty { get; } = new(null, null, null);

    public string FormatResetCountdown()
    {
        if (ResetsAt is null or <= 0) return Strings.Unknown;
        var delta = DateTimeOffset.FromUnixTimeSeconds(ResetsAt.Value) - DateTimeOffset.Now;
        if (delta <= TimeSpan.Zero) return Strings.Expired;
        var minutes = (int)Math.Floor(delta.TotalMinutes);
        return minutes >= 60 ? Strings.Format("DurationHoursMinutes", minutes / 60, minutes % 60) : Strings.Format("DurationMinutes", minutes);
    }
}

public sealed record CreditsSnapshot(bool HasCredits, bool Unlimited, string? Balance);

/// <summary>
/// A reserve/backup bucket from <c>rateLimitsByLimitId</c> (for example the Luna Reserve
/// weekly limit). It is only used after the regular Codex quota is exhausted.
/// </summary>
public sealed record ReserveLimitSnapshot(string LimitId, string? LimitName, RateLimitWindow Primary, RateLimitWindow Secondary)
{
    private const int WeeklyWindowDurationMins = 7 * 24 * 60;
    public const string LunaLimitId = "luna";

    /// <summary>Percentage of the reserve bucket that is still available.</summary>
    public int? RemainingPercent => Primary.UsedPercent is null ? null : Math.Clamp(100 - Primary.UsedPercent.Value, 0, 100);
    public bool HasRemaining => RemainingPercent is > 0;
    public bool IsWeekly => Primary.WindowDurationMins == WeeklyWindowDurationMins || Secondary.WindowDurationMins == WeeklyWindowDurationMins;
    public bool IsLuna => LimitId.Contains(LunaLimitId, StringComparison.OrdinalIgnoreCase)
        || (LimitName?.Contains("luna", StringComparison.OrdinalIgnoreCase) ?? false);
    /// <summary>Human readable label; falls back to the generic localized name when the bucket has no name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(LimitName) ? Strings.Get("LunaReserveLimit") : LimitName!;
}

public sealed record RateLimitSnapshot(string LimitId, string? LimitName, string? PlanType, string? RateLimitReachedType, CreditsSnapshot? Credits, RateLimitWindow Primary, RateLimitWindow Secondary, int? RateLimitResetCredits, ReserveLimitSnapshot? Reserve = null)
{
    private const int FiveHourWindowDurationMins = 5 * 60;

    public int? RemainingPercent => Primary.UsedPercent is null ? null : Math.Clamp(100 - Primary.UsedPercent.Value, 0, 100);
    public bool HasFiveHourLimit => Primary.WindowDurationMins == FiveHourWindowDurationMins || Secondary.WindowDurationMins == FiveHourWindowDurationMins;

    /// <summary>The regular Codex quota is used up, so the reserve bucket takes over the display.</summary>
    public bool IsPrimaryExhausted => Primary.UsedPercent is >= 100 || !string.IsNullOrWhiteSpace(RateLimitReachedType);
    /// <summary>The reserve (Luna) bucket should be displayed instead of the exhausted regular quota.</summary>
    public bool IsReserveActive => Reserve is { HasRemaining: true } && (IsPrimaryExhausted || Primary.UsedPercent is null);
    /// <summary>The bucket that currently backs the headline: either the regular quota or the reserve bucket.</summary>
    public RateLimitWindow ActivePrimary => IsReserveActive && Reserve is { } reserve ? reserve.Primary : Primary;
    public int? ActiveRemainingPercent => ActivePrimary.UsedPercent is null ? null : Math.Clamp(100 - ActivePrimary.UsedPercent.Value, 0, 100);
    public string ActiveDisplayName => IsReserveActive && Reserve is { } reserve ? reserve.DisplayName : Strings.Get("PrimaryLimit");

    public static RateLimitSnapshot FromJson(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(Strings.Get("RateLimitResponseNotObject"));
        JsonElement snapshot;
        var hasBucketMap = false;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out var codex)) { snapshot = codex; hasBucketMap = true; }
        else if (result.TryGetProperty("rateLimits", out var limits) && limits.ValueKind == JsonValueKind.Object) snapshot = limits;
        else throw new InvalidOperationException(Strings.Get("RateLimitBucketMissing"));

        if (snapshot.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(Strings.Get("RateLimitBucketInvalid"));
        var primary = snapshot.TryGetProperty("primary", out var primaryJson) ? ReadWindow(primaryJson) : RateLimitWindow.Empty;
        var secondary = snapshot.TryGetProperty("secondary", out var secondaryJson) ? ReadWindow(secondaryJson) : RateLimitWindow.Empty;
        CreditsSnapshot? credits = null;
        if (snapshot.TryGetProperty("credits", out var creditJson) && creditJson.ValueKind == JsonValueKind.Object)
            credits = new(AsBool(creditJson, "hasCredits"), AsBool(creditJson, "unlimited"), AsString(creditJson, "balance"));
        int? resetCredits = null;
        if (result.TryGetProperty("rateLimitResetCredits", out var resetJson) && resetJson.ValueKind == JsonValueKind.Object && resetJson.TryGetProperty("availableCount", out var count) && count.TryGetInt32(out var value)) resetCredits = value;
        var reserve = hasBucketMap ? ReadReserveLimit(byId) : null;
        return new(AsString(snapshot, "limitId") ?? "codex", AsString(snapshot, "limitName"), AsString(snapshot, "planType"), AsString(snapshot, "rateLimitReachedType"), credits, primary, secondary, resetCredits, reserve);
    }

    /// <summary>
    /// Finds the reserve bucket (for example "Luna Reserve Weekly") inside the
    /// <c>rateLimitsByLimitId</c> map. The regular "codex" bucket is never returned.
    /// </summary>
    private static ReserveLimitSnapshot? ReadReserveLimit(JsonElement bucketMap)
    {
        ReserveLimitSnapshot? best = null;
        var bestScore = 0;
        foreach (var bucket in bucketMap.EnumerateObject())
        {
            if (bucket.Value.ValueKind != JsonValueKind.Object) continue;
            var limitId = AsString(bucket.Value, "limitId") ?? bucket.Name;
            if (limitId.Equals("codex", StringComparison.OrdinalIgnoreCase)) continue;
            var limitName = AsString(bucket.Value, "limitName");
            var candidate = new ReserveLimitSnapshot(limitId, limitName, ReadNamedWindow(bucket.Value, "primary"), ReadNamedWindow(bucket.Value, "secondary"));
            var score = ScoreReserveLimit(candidate);
            if (score > bestScore) { best = candidate; bestScore = score; }
        }
        return best;
    }

    /// <summary>Scores how strongly a bucket looks like the Luna reserve quota; 0 means "not a reserve bucket".</summary>
    private static int ScoreReserveLimit(ReserveLimitSnapshot candidate)
    {
        var score = 0;
        if (candidate.IsLuna) score += 4;
        if (candidate.LimitId.Contains("reserve", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (candidate.LimitName?.Contains("reserve", StringComparison.OrdinalIgnoreCase) == true) score += 3;
        if (candidate.LimitName?.Contains("weekly", StringComparison.OrdinalIgnoreCase) == true) score += 2;
        if (candidate.IsWeekly) score += 1;
        if (candidate.Primary.UsedPercent is not null) score += 1;
        return score;
    }

    public string FormatMultiline()
    {
        var sb = new StringBuilder(Strings.Format("StatusPlan", PlanType ?? "unknown"));
        sb.AppendLine().Append(Strings.Format("StatusLimitId", LimitId));
        if (Primary.UsedPercent is not null) sb.AppendLine().Append(Strings.Format("StatusPrimaryUsage", Primary.UsedPercent, RemainingPercent)).AppendLine().Append(Strings.Format("StatusPrimaryReset", Primary.FormatResetCountdown()));
        if (Secondary.UsedPercent is not null) sb.AppendLine().Append(Strings.Format("StatusSecondaryUsage", Secondary.UsedPercent)).AppendLine().Append(Strings.Format("StatusSecondaryReset", Secondary.FormatResetCountdown()));
        if (Reserve is { } reserve)
        {
            sb.AppendLine().Append(Strings.Format("StatusReserveUsage", reserve.DisplayName, reserve.Primary.UsedPercent, reserve.RemainingPercent));
            sb.AppendLine().Append(Strings.Format("StatusReserveReset", reserve.Primary.FormatResetCountdown()));
            if (IsReserveActive) sb.AppendLine().Append(Strings.Format("StatusReserveActive", reserve.RemainingPercent));
        }
        if (Credits is not null) sb.AppendLine().Append(Strings.Format("StatusCredits", Credits.Unlimited ? Strings.Get("Unlimited") : Credits.Balance ?? "0"));
        if (RateLimitReachedType is not null) sb.AppendLine().Append(Strings.Format("StatusReached", RateLimitReachedType));
        if (RateLimitResetCredits is not null) sb.AppendLine().Append(Strings.Format("StatusResetCredits", RateLimitResetCredits));
        return sb.ToString();
    }

    private static RateLimitWindow ReadNamedWindow(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var window) ? ReadWindow(window) : RateLimitWindow.Empty;

    private static RateLimitWindow ReadWindow(JsonElement element) => element.ValueKind == JsonValueKind.Object ? new(AsInt(element, "usedPercent"), AsInt(element, "windowDurationMins"), AsLong(element, "resetsAt")) : RateLimitWindow.Empty;
    private static string? AsString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool AsBool(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    private static int? AsInt(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static long? AsLong(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
}

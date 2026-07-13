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

public sealed record RateLimitSnapshot(string LimitId, string? LimitName, string? PlanType, string? RateLimitReachedType, CreditsSnapshot? Credits, RateLimitWindow Primary, RateLimitWindow Secondary, int? RateLimitResetCredits)
{
    private const int FiveHourWindowDurationMins = 5 * 60;

    public int? RemainingPercent => Primary.UsedPercent is null ? null : Math.Clamp(100 - Primary.UsedPercent.Value, 0, 100);
    public bool HasFiveHourLimit => Primary.WindowDurationMins == FiveHourWindowDurationMins || Secondary.WindowDurationMins == FiveHourWindowDurationMins;

    public static RateLimitSnapshot FromJson(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(Strings.Get("RateLimitResponseNotObject"));
        JsonElement snapshot;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out var codex)) snapshot = codex;
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
        return new(AsString(snapshot, "limitId") ?? "codex", AsString(snapshot, "limitName"), AsString(snapshot, "planType"), AsString(snapshot, "rateLimitReachedType"), credits, primary, secondary, resetCredits);
    }

    public string FormatMultiline()
    {
        var sb = new StringBuilder(Strings.Format("StatusPlan", PlanType ?? "unknown"));
        sb.AppendLine().Append(Strings.Format("StatusLimitId", LimitId));
        if (Primary.UsedPercent is not null) sb.AppendLine().Append(Strings.Format("StatusPrimaryUsage", Primary.UsedPercent, RemainingPercent)).AppendLine().Append(Strings.Format("StatusPrimaryReset", Primary.FormatResetCountdown()));
        if (Secondary.UsedPercent is not null) sb.AppendLine().Append(Strings.Format("StatusSecondaryUsage", Secondary.UsedPercent)).AppendLine().Append(Strings.Format("StatusSecondaryReset", Secondary.FormatResetCountdown()));
        if (Credits is not null) sb.AppendLine().Append(Strings.Format("StatusCredits", Credits.Unlimited ? Strings.Get("Unlimited") : Credits.Balance ?? "0"));
        if (RateLimitReachedType is not null) sb.AppendLine().Append(Strings.Format("StatusReached", RateLimitReachedType));
        if (RateLimitResetCredits is not null) sb.AppendLine().Append(Strings.Format("StatusResetCredits", RateLimitResetCredits));
        return sb.ToString();
    }

    private static RateLimitWindow ReadWindow(JsonElement element) => element.ValueKind == JsonValueKind.Object ? new(AsInt(element, "usedPercent"), AsInt(element, "windowDurationMins"), AsLong(element, "resetsAt")) : RateLimitWindow.Empty;
    private static string? AsString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool AsBool(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    private static int? AsInt(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static long? AsLong(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
}

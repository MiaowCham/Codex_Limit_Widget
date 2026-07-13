using System.Globalization;
using System.Resources;

namespace CodexLimitWidget.Core.Resources;

public static class Strings
{
    private static readonly ResourceManager ResourceManager = new("CodexLimitWidget.Core.Resources.Strings", typeof(Strings).Assembly);
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    public static string Get(string key)
    {
        var culture = CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName is not ("en" or "zh" or "ja")) culture = EnglishCulture;
        return ResourceManager.GetString(key, culture) ?? ResourceManager.GetString(key, EnglishCulture) ?? $"[{key}]";
    }
    public static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);

    public static string Unknown => Get(nameof(Unknown));
    public static string Expired => Get(nameof(Expired));
    public static string Loading => Get(nameof(Loading));
    public static string TooltipPrimaryUsage => Get(nameof(TooltipPrimaryUsage));
    public static string TooltipDisableTopmost => Get(nameof(TooltipDisableTopmost));
    public static string TooltipEnableTopmost => Get(nameof(TooltipEnableTopmost));
    public static string TooltipRefresh => Get(nameof(TooltipRefresh));
    public static string TooltipClose => Get(nameof(TooltipClose));
    public static string TooltipUsageColors => Get(nameof(TooltipUsageColors));
    public static string OpenTiboXProfile => Get(nameof(OpenTiboXProfile));
    public static string TrayHideWindow => Get(nameof(TrayHideWindow));
    public static string TrayRefresh => Get(nameof(TrayRefresh));
    public static string TrayExit => Get(nameof(TrayExit));
}

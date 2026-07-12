using System.Globalization;
using CodexLimitWidget.Core;
using CodexLimitWidget.Core.Resources;
using Xunit;

namespace CodexLimitWidget.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void EnglishResourcesAreLoadedForEnglishCulture()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("Unknown", Strings.Unknown);
            Assert.Equal("75% Left", Strings.Format("RemainingPercent", 75));
            Assert.Equal("Reset in 12:34", Strings.Format("ResetAt", "12:34"));
            Assert.Equal("Weekly limit 50%, 07/13 12:34", Strings.Format("WeeklyLimit", 50, "07/13 12:34"));
            Assert.Equal("Updated at 12:34:56", Strings.Format("UpdatedAt", "12:34:56"));
            Assert.Equal("version 1.2.3", Strings.Format("TrayVersion", "1.2.3"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TraditionalChineseResourcesAreLoadedForZhHantCulture()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hant");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-Hant");

            Assert.Equal("剩餘 75%", Strings.Format("RemainingPercent", 75));
            Assert.Equal("將於 12:34 重設", Strings.Format("ResetAt", "12:34"));
            Assert.Equal("每週限額 50% · 7 月 13 日 12:34", Strings.Format("WeeklyLimit", 50, "7 月 13 日 12:34"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void SimplifiedChineseResourcesAreLoadedForZhHansCulture()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hans");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-Hans");

            Assert.Equal("剩余 75%", Strings.Format("RemainingPercent", 75));
            Assert.Equal("将于 12:34 重置", Strings.Format("ResetAt", "12:34"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void JapaneseResourcesAreLoadedForJapaneseCulture()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");

            Assert.Equal("残り 75%", Strings.Format("RemainingPercent", 75));
            Assert.Equal("12:34 にリセット", Strings.Format("ResetAt", "12:34"));
            Assert.Equal("週 50% · 7/13 12:34", Strings.Format("WeeklyLimit", 50, "7/13 12:34"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void UnsupportedCulturesFallbackToEnglish()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("75% Left", Strings.Format("RemainingPercent", 75));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void LanguageParameterCanSetTheCurrentCulture()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var previousDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            Assert.True(Localization.TrySetCulture("en-US"));
            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
            Assert.False(Localization.TrySetCulture("not-a-culture"));
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = previousDefaultCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void ResetCountdownUsesLocalizedTemplatesForUnknownExpiredAndDurations()
    {
        Assert.Equal(Strings.Unknown, new RateLimitWindow(null, null, null).FormatResetCountdown());
        Assert.Equal(Strings.Expired, new RateLimitWindow(null, null, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()).FormatResetCountdown());
        Assert.Equal(Strings.Format("DurationMinutes", 30), new RateLimitWindow(null, null, DateTimeOffset.UtcNow.AddMinutes(30.5).ToUnixTimeSeconds()).FormatResetCountdown());
        Assert.Equal(Strings.Format("DurationHoursMinutes", 2, 5), new RateLimitWindow(null, null, DateTimeOffset.UtcNow.AddMinutes(125.5).ToUnixTimeSeconds()).FormatResetCountdown());
    }
}

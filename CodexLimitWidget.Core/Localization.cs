using System.Globalization;

namespace CodexLimitWidget.Core;

public static class Localization
{
    public static bool TrySetCulture(string cultureName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName, predefinedOnly: true);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}

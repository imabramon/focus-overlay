using System.Globalization;
using FocusOverlay.Models;

namespace FocusOverlay.Services;

public static class LanguageManager
{
    private static readonly CultureInfo _systemCulture = CultureInfo.CurrentUICulture;

    public static void Apply(AppLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(Resolve(language));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static string Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Russian => "ru",
        AppLanguage.English => "en",
        _ => _systemCulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en",
    };
}

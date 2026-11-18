using System.Globalization;
using System.Windows;
using DoubleClickHealer.Core.Config;

namespace DoubleClickHealer.App.Services;

internal static class LocalizationService
{
    private static readonly string[] DictionaryNames =
    [
        "Strings.zh-CN.xaml",
        "Strings.en-US.xaml",
    ];

    public static AppLanguage Current { get; private set; } = AppLanguage.Chinese;

    public static void Initialize(AppLanguage language)
    {
        Current = language;

        CultureInfo culture = CultureInfo.GetCultureInfo(
            language == AppLanguage.English ? "en-US" : "zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (Application.Current == null) return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            string? source = dictionaries[i].Source?.OriginalString;
            if (source != null && DictionaryNames.Any(name =>
                    source.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
            {
                dictionaries.RemoveAt(i);
            }
        }

        string dictionary = language == AppLanguage.English
            ? "Themes/Strings.en-US.xaml"
            : "Themes/Strings.zh-CN.xaml";

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(dictionary, UriKind.Relative),
        });
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);
}

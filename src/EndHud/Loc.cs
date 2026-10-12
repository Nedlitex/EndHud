using System.IO;
using System.Windows;

namespace EndHud;

/// <summary>
/// UI strings. Each language is a ResourceDictionary at i18n/&lt;language&gt;.xaml; XAML uses
/// {DynamicResource Key}, code uses <see cref="Get"/> / <see cref="Format"/>.
/// To add a language: copy i18n/zh-CN.xaml, translate the values, set "Language" in config.json.
/// </summary>
internal static class Loc
{
    public const string DefaultLanguage = "zh-CN";

    public static void Load(string language)
    {
        var dictionary = TryLoad(language) ?? TryLoad(DefaultLanguage)
            ?? throw new InvalidOperationException($"Missing i18n/{DefaultLanguage}.xaml");
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }

    public static string Get(string key) => Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    private static ResourceDictionary? TryLoad(string language)
    {
        try
        {
            return new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/i18n/{language}.xaml", UriKind.Absolute),
            };
        }
        catch (IOException)
        {
            return null;
        }
    }
}

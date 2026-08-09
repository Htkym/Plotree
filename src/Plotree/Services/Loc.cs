using Microsoft.Windows.ApplicationModel.Resources;

namespace Plotree.Services;

/// <summary>
/// Localization helper for code-behind and ViewModel strings.
/// XAML statics use x:Uid; both resolve through the same MRT Core resources,
/// honoring the language override applied at startup in <see cref="App"/>.
/// </summary>
public static class Loc
{
    private static readonly ResourceLoader Loader = new();

    /// <summary>Returns the localized string for a key, or the key itself when missing.</summary>
    public static string Get(string key)
    {
        try
        {
            var value = Loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    /// <summary>Returns the localized string for a key formatted with arguments.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(Get(key), args);
}

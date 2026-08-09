using System.Text.Json;

namespace Plotree.Services;

/// <summary>
/// Persists the recent-files list and app settings (language) to
/// %LOCALAPPDATA%\Plotree\settings.json.
/// </summary>
public class RecentFilesService
{
    /// <summary>Reads the persisted language override ("ja-JP"/"en-US"), or null for system default.</summary>
    public static string? GetLanguageOverride()
    {
        var settings = LoadSettings();
        return string.IsNullOrWhiteSpace(settings.Language) ? null : settings.Language;
    }

    /// <summary>Persists the language override; null or empty means system default.</summary>
    public static void SetLanguageOverride(string? language)
    {
        try
        {
            var settings = LoadSettings();
            settings.Language = string.IsNullOrWhiteSpace(language) ? null : language;
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonContext.AppSettings));
        }
        catch (Exception)
        {
            // Persisting settings is best-effort.
        }
    }

    private const int MaxRecentFiles = 10;

    private static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plotree");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly PlotreeJsonContext JsonContext = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

    private readonly List<string> _recentFiles;

    public RecentFilesService()
    {
        _recentFiles = Load();
    }

    public IReadOnlyList<string> RecentFiles => _recentFiles;

    /// <summary>Adds (or moves) a path to the top of the list and persists it.</summary>
    public void Add(string path)
    {
        _recentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentFiles.Insert(0, path);
        if (_recentFiles.Count > MaxRecentFiles)
        {
            _recentFiles.RemoveRange(MaxRecentFiles, _recentFiles.Count - MaxRecentFiles);
        }

        Save();
    }

    /// <summary>Removes a path (e.g. when the file no longer exists) and persists the list.</summary>
    public void Remove(string path)
    {
        if (_recentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    private static List<string> Load() => LoadSettings().RecentFiles;

    private static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), JsonContext.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt settings are non-fatal — start fresh.
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            var settings = LoadSettings(); // preserve other settings (language)
            settings.RecentFiles = _recentFiles;
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonContext.AppSettings));
        }
        catch (Exception)
        {
            // Persisting recents is best-effort.
        }
    }

    internal sealed class AppSettings
    {
        public List<string> RecentFiles { get; set; } = [];

        public string? Language { get; set; }
    }
}

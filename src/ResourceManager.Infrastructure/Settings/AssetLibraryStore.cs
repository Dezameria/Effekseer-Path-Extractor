using System.Text.Json;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Infrastructure.Settings;

public sealed record SavedAssetLibrary(string Name, string Path)
{
    public string Display => Name + " — " + Path;
}
public sealed record AssetLibrarySettings(int Version, IReadOnlyList<SavedAssetLibrary> Libraries,
    string? SelectedPath = null, string? OutputRoot = null);

public sealed class AssetLibraryStore(string? settingsPath = null)
{
    public string SettingsPath { get; } = settingsPath ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EffekseerResourceManager", "settings.json");

    public AssetLibrarySettings Load()
    {
        if (!File.Exists(SettingsPath)) return new(1, []);
        if (WindowsPathResolver.HasReparsePoint(SettingsPath)) throw new IOException("Settings path contains a link.");
        if (new FileInfo(SettingsPath).Length > 1024 * 1024) throw new InvalidDataException("Settings file is too large.");
        var settings = JsonSerializer.Deserialize<AssetLibrarySettings>(File.ReadAllText(SettingsPath));
        if (settings is null || settings.Version != 1 || settings.Libraries is null) throw new InvalidDataException("Unsupported asset settings.");
        return settings;
    }

    public void Save(AssetLibrarySettings settings)
    {
        if (WindowsPathResolver.HasReparsePoint(SettingsPath)) throw new IOException("Settings path contains a link.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

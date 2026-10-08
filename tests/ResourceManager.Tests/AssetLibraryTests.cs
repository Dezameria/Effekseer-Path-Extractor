using ResourceManager.Infrastructure.Settings;

namespace ResourceManager.Tests;

public sealed class AssetLibraryTests
{
    [Fact]
    public void SavedLibrariesAndOutputPathSurviveReload()
    {
        using var fixture = new TestWorkspace();
        var path = Path.Combine(fixture.Root, "settings.json");
        var store = new AssetLibraryStore(path);
        Assert.Empty(store.Load().Libraries);
        var asset = new SavedAssetLibrary("คลังหลัก", Path.Combine(fixture.Root, "Assets"));
        store.Save(new(1, [asset], asset.Path, "D:\\Exports"));
        var reloaded = new AssetLibraryStore(path).Load();
        Assert.Equal(asset, Assert.Single(reloaded.Libraries));
        Assert.Equal(asset.Path, reloaded.SelectedPath);
        Assert.Equal("D:\\Exports", reloaded.OutputRoot);
        store.Save(new(1, []));
        Assert.Empty(store.Load().Libraries);
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public void UnsupportedSettingsVersionIsReportedWithoutReplacingTheFile()
    {
        using var fixture = new TestWorkspace();
        var path = fixture.File("settings.json", "{\"Version\":999,\"Libraries\":[]}");
        var before = File.ReadAllText(path);
        Assert.Throws<InvalidDataException>(() => new AssetLibraryStore(path).Load());
        Assert.Equal(before, File.ReadAllText(path));
    }
}

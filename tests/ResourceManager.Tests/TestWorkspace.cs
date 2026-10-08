namespace ResourceManager.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "EffekseerResourceManager.Tests", Guid.NewGuid().ToString("N"));
    public TestWorkspace() => Directory.CreateDirectory(Root);
    public string File(string relative, string content = "fixture")
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal static class Samples
{
    public static string? Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("EVFX_SAMPLE_ROOT");
            if (configured is not null && Directory.Exists(configured)) return configured;
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "effekseer", "EVFX Shineforge");
                if (Directory.Exists(path)) return path;
            }
            return null;
        }
    }
}

public sealed class SampleFactAttribute : FactAttribute
{
    public SampleFactAttribute()
    { if (Samples.Root is null) Skip = "Local EVFX fixtures absent; set EVFX_SAMPLE_ROOT to run this integration test."; }
}

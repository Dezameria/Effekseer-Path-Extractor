using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.FileSystem;
using ResourceManager.Infrastructure.Search;

namespace ResourceManager.Tests;

public sealed class PathAndSearchTests
{
    [Fact]
    public void RelativeInsideResolvesFromOwnerNotWorkingDirectory()
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("Bloom/main.efkefc");
        var target = fixture.File("Bloom/Textures/กลีบ.png");
        var audit = new WindowsPathResolver().Audit(owner, "Textures/./../Textures/กลีบ.png", Path.GetDirectoryName(owner)!);
        Assert.Equal("VALID", audit.Status);
        Assert.Equal(target, audit.ResolvedPath);
        Assert.False(audit.IsAbsolute);
    }

    [Fact]
    public void ExistingExternalIsNeverValid()
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("Bloom/main.efkefc");
        var outside = fixture.File("Old/smoke.png");
        var audit = new WindowsPathResolver().Audit(owner, outside, Path.GetDirectoryName(owner)!);
        Assert.True(audit.Exists);
        Assert.False(audit.InsideBoundary);
        Assert.Equal("EXTERNAL", audit.Status);
    }

    [Fact]
    public void SiblingPrefixAndTraversalEscapeBoundary()
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("Bloom/main.efkefc");
        fixture.File("Bloom2/smoke.png");
        Assert.False(WindowsPathResolver.IsInside(Path.Combine(fixture.Root, "Bloom2", "smoke.png"), Path.GetDirectoryName(owner)!));
        Assert.Equal("EXTERNAL", new WindowsPathResolver().Audit(owner, "../Bloom2/smoke.png", Path.GetDirectoryName(owner)!).Status);
    }

    [Fact]
    public void MissingRemainsMissing()
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("main.efkefc");
        Assert.Equal("MISSING", new WindowsPathResolver().Audit(owner, "Texture/missing.png", fixture.Root).Status);
    }

    [Theory]
    [InlineData("C:foo.png")]
    [InlineData("\\foo.png")]
    [InlineData("")]
    public void AmbiguousResolutionContextIsNotGuessed(string reference)
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("main.efkefc");
        Assert.Equal("UNRESOLVED", new WindowsPathResolver().Audit(owner, reference, fixture.Root).Status);
    }

    [Fact]
    public void AbsoluteInternalIsMarkedAsBoundToOriginalLocation()
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("main.efkefc");
        var target = fixture.File("smoke.png");
        var audit = new WindowsPathResolver().Audit(owner, target, fixture.Root);
        Assert.Equal("VALID", audit.Status);
        Assert.True(audit.IsAbsolute);
        Assert.NotNull(audit.Reason);
    }

    [Fact]
    public void WindowsCaseDoesNotChangeBoundaryResult()
    {
        using var fixture = new TestWorkspace();
        Assert.True(WindowsPathResolver.IsInside(Path.Combine(fixture.Root, "Textures", "a.png").ToUpperInvariant(), fixture.Root.ToLowerInvariant()));
    }

    [Fact]
    public void JunctionIsNeverCertifiedAsInternal()
    {
        using var fixture = new TestWorkspace();
        var owner = fixture.File("Bloom/main.efkefc");
        var external = fixture.File("External/smoke.png");
        var link = Path.Combine(Path.GetDirectoryName(owner)!, "Linked");
        // Junctions are available on Windows without enabling developer-mode symlinks.
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(Path.GetDirectoryName(external)!);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        var audit = new WindowsPathResolver().Audit(owner, "Linked/smoke.png", Path.GetDirectoryName(owner)!);
        Assert.True(audit.HasLink);
        Assert.False(audit.InsideBoundary);
        Assert.Equal("UNRESOLVED", audit.Status);
        Directory.Delete(link);
    }

    [Fact]
    public async Task SearchReturnsAllCandidatesWithoutSelectingOrRewriting()
    {
        using var fixture = new TestWorkspace();
        var one = fixture.File("Search/Common/noise.png", "a");
        var two = fixture.File("Search/Fire/noise.png", "b");
        var missing = new ResourceEntry("missing-id", ResourceType.Texture, "noise.png", null, false, false, "MISSING", null, 1, []);
        var result = await new MissingResourceFinder().FindAsync([missing], Path.Combine(fixture.Root, "Search"));
        var match = Assert.Single(result.Matches);
        Assert.True(match.IsAmbiguous);
        Assert.Equal(2, match.Candidates.Count);
        Assert.Equal(new[] { one, two }.Order(), match.Candidates.Select(c => c.Path).Order());
        Assert.Equal("MISSING", missing.Status);
    }
}

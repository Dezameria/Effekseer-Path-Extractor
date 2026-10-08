using ResourceManager.Core.Abstractions;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Tests;

public sealed class ScannerTests
{
    [Fact]
    public async Task NestedMissingIsDetectedAndOwnerBaseIsPreserved()
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("main.efkefc");
        var material = fixture.File("Materials/water.efkmat");
        var parser = new FixtureParser(new Dictionary<string, ParsedDocument>
        {
            [effect] = Document(effect, [Reference(effect, "Materials/water.efkmat", ResourceType.Material)]),
            [material] = Document(material, [Reference(material, "../Texture/missing.png")])
        });
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([effect], fixture.Root);
        Assert.Equal(2, report.Documents.Count);
        var missing = Assert.Single(report.Resources, r => r.Status == "MISSING");
        Assert.Equal(Path.Combine(fixture.Root, "Texture", "missing.png"), missing.ResolvedPath);
        Assert.Equal(2, Assert.Single(missing.References).Depth);
        Assert.Equal(PortabilityOutcome.Fail, report.Portability.Outcome);
    }

    [Fact]
    public async Task CyclesDoNotLoseEdgesOrScanForever()
    {
        using var fixture = new TestWorkspace();
        var a = fixture.File("a.efkefc");
        var b = fixture.File("b.efkefc");
        var parser = new FixtureParser(new Dictionary<string, ParsedDocument>
        {
            [a] = Document(a, [Reference(a, "b.efkefc", ResourceType.Effect)]),
            [b] = Document(b, [Reference(b, "a.efkefc", ResourceType.Effect)])
        });
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([a], fixture.Root);
        Assert.Equal(2, report.Documents.Count);
        Assert.Equal(2, report.Resources.Sum(r => r.ReferenceCount));
        Assert.Equal(PortabilityOutcome.Pass, report.Portability.Outcome);
    }

    [Fact]
    public async Task SameTextFromDifferentOwnersHasDifferentTargets()
    {
        using var fixture = new TestWorkspace();
        var a = fixture.File("Fire/main.efkefc");
        var b = fixture.File("Ice/main.efkefc");
        fixture.File("Fire/smoke.png");
        fixture.File("Ice/smoke.png");
        var parser = new FixtureParser(new Dictionary<string, ParsedDocument>
        {
            [a] = Document(a, [Reference(a, "smoke.png")]),
            [b] = Document(b, [Reference(b, "smoke.png")])
        });
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([a, b], fixture.Root);
        Assert.Equal(2, report.Resources.Count);
        Assert.All(report.Resources, r => Assert.Equal("VALID", r.Status));
    }

    [Fact]
    public async Task AbsoluteInternalCannotPassRelocation()
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("main.efkefc");
        var texture = fixture.File("smoke.png");
        var parser = new FixtureParser(new Dictionary<string, ParsedDocument> { [effect] = Document(effect, [Reference(effect, texture)]) });
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([effect], fixture.Root);
        Assert.Equal("VALID", Assert.Single(report.Resources).Status);
        Assert.Equal(PortabilityOutcome.Fail, report.Portability.Outcome);
    }

    [Fact]
    public async Task IncompleteCoverageCannotPassEvenWithAllFilesFound()
    {
        using var fixture = new TestWorkspace();
        var effect = fixture.File("main.efkefc");
        var parser = new FixtureParser(new Dictionary<string, ParsedDocument> { [effect] = Document(effect, []) with { Coverage = ScanCoverage.MetadataOnly } });
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([effect], fixture.Root);
        Assert.Equal(PortabilityOutcome.Inconclusive, report.Portability.Outcome);
    }

    [Fact]
    public async Task MissingDefaultDoesNotFalselyProveRuntimeFailure()
    {
        using var fixture = new TestWorkspace();
        var material = fixture.File("water.efkmat");
        var reference = Reference(material, "missing.png") with { Role = ReferenceRole.MaterialDefault };
        var parser = new FixtureParser(new Dictionary<string, ParsedDocument> { [material] = Document(material, [reference]) with { Coverage = ScanCoverage.MetadataOnly } });
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync([material], fixture.Root);
        Assert.Equal("MISSING", Assert.Single(report.Resources).Status);
        Assert.Equal(PortabilityOutcome.Inconclusive, report.Portability.Outcome);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var parser = new FixtureParser([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ResourceScanner(parser, new WindowsPathResolver())
            .ScanAsync([Path.GetFullPath("main.efkefc")], Path.GetFullPath("."), cancellationToken: cancelled.Token));
    }

    private static ParsedDocument Document(string path, ResourceReference[] references) =>
        new(path, "Fixture", 1, null, null, ScanCoverage.Complete, false, [], references, []);
    private static ResourceReference Reference(string owner, string path, ResourceType type = ResourceType.Texture) => new(owner, "fixture/reference", path, type, ReferenceRole.Runtime);
    private sealed class FixtureParser(Dictionary<string, ParsedDocument> documents) : IResourceParser
    {
        public bool CanRead(string path) => documents.ContainsKey(path);
        public Task<ParsedDocument> ParseAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(documents[path]);
    }
}

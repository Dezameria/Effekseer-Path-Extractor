using System.Security.Cryptography;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("45464B45")]
    [InlineData("45464B4500000000494E464FFFFFFFFF")]
    [InlineData("45464B4500000000494E464F04000000")]
    [InlineData("45464B4500000000494E464F040000004A060000")]
    public async Task TruncatedAndMalformedContainersReturnDiagnostics(string hex)
    {
        using var fixture = new TestWorkspace();
        var path = fixture.File("bad.efkefc");
        await File.WriteAllBytesAsync(path, Convert.FromHexString(hex));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var result = await new EffekseerResourceParser().ParseAsync(path);
        Assert.Equal(ScanCoverage.Error, result.Coverage);
        Assert.False(result.CanRewrite);
        Assert.Contains(result.Diagnostics, d => d.IsError);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task UnknownContainerVersionIsUnsupported()
    {
        using var fixture = new TestWorkspace();
        var path = fixture.File("future.efkefc");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("45464B4563000000"));
        var result = await new EffekseerResourceParser().ParseAsync(path);
        Assert.Equal(ScanCoverage.Unsupported, result.Coverage);
        Assert.Empty(result.References);
        Assert.False(result.CanRewrite);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToParseError()
    {
        using var fixture = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EffekseerResourceParser().ParseAsync(fixture.File("main.efkefc"), cancellation.Token));
    }

    [SampleFact]
    public async Task LocalCorpusGoldenDependenciesAndSourceIntegrity()
    {
        var root = Samples.Root!;
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        var before = files.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        var parser = new EffekseerResourceParser();
        var discovery = DocumentDiscovery.Enumerate(root, parser.CanRead);
        Assert.Empty(discovery.Diagnostics);
        var report = await new ResourceScanner(parser, new WindowsPathResolver()).ScanAsync(discovery.Files, root);
        Assert.Equal(19, report.Documents.Count);
        Assert.Equal(17, report.Documents.Count(d => d.Format == "EFKE"));
        Assert.Equal(2, report.Documents.Count(d => d.Format == "EFKM"));
        Assert.Equal(42, report.Resources.Count);
        Assert.Equal(38, report.Resources.Count(r => r.Status == "VALID"));
        Assert.Equal(4, report.Resources.Count(r => r.Status == "MISSING"));
        Assert.DoesNotContain(report.Resources, r => r.Status == "EXTERNAL");
        Assert.DoesNotContain(report.Diagnostics, d => d.IsError);
        Assert.Equal(PortabilityOutcome.Inconclusive, report.Portability.Outcome);
        Assert.All(report.Documents, d => { Assert.False(d.CanRewrite); Assert.Equal(ScanCoverage.MetadataOnly, d.Coverage); });
        var effects = report.Documents.Where(d => d.Format == "EFKE").ToArray();
        Assert.Equal(15, effects.Count(d => d.DependencyVersion == 1610 && d.EditorVersion == "1.62a"));
        Assert.Equal(2, effects.Count(d => d.DependencyVersion == 1810 && d.EditorVersion == "1.80.7"));
        foreach (var effect in effects)
        {
            var metadata = effect.References.Where(r => r.Role == ReferenceRole.Metadata).Select(r => (r.Type, r.OriginalReference)).ToHashSet();
            var runtime = effect.References.Where(r => r.Role == ReferenceRole.Runtime).Select(r => (r.Type, r.OriginalReference)).ToHashSet();
            Assert.True(metadata.SetEquals(runtime));
        }
        Assert.DoesNotContain(report.Resources.Where(r => r.Status == "MISSING").SelectMany(r => r.References),
            r => r.Reference.Role is ReferenceRole.Runtime or ReferenceRole.CompatibilityRuntime or ReferenceRole.Metadata);
        Assert.All(files, p => Assert.Equal(before[p], SHA256.HashData(File.ReadAllBytes(p))));
    }

    [SampleFact]
    public async Task MaterialDefaultsAreSeparateFromEffectRuntime()
    {
        var material = await new EffekseerResourceParser().ParseAsync(Path.Combine(Samples.Root!, "Materials", "Dissolve.efkmat"));
        Assert.Contains(material.References, r => r.Role == ReferenceRole.MaterialDefault && r.OriginalReference == "../Textures/Check01.png");
        Assert.Contains(material.References, r => r.Role == ReferenceRole.MaterialDefault && r.OriginalReference == "../Textures/Distortion01.png");
        Assert.All(material.References, r => Assert.True(r.Role is ReferenceRole.MaterialDefault or ReferenceRole.MaterialEditor));
    }

    [SampleFact]
    public async Task CorruptEditorCannotBeMistakenForSuccessfulAudit()
    {
        using var fixture = new TestWorkspace();
        var source = Path.Combine(Samples.Root!, "annihilation_flare.efkefc");
        var original = await new EffekseerResourceParser().ParseAsync(source);
        var data = await File.ReadAllBytesAsync(source);
        var edit = Assert.Single(original.Chunks, c => c.Id == "EDIT");
        data[edit.Offset] = 0;
        var path = fixture.File("corrupt.efkefc");
        await File.WriteAllBytesAsync(path, data);
        var result = await new EffekseerResourceParser().ParseAsync(path);
        Assert.Equal(ScanCoverage.Error, result.Coverage);
    }
}

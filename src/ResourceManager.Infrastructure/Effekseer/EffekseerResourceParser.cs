using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ResourceManager.Core.Abstractions;
using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Infrastructure.Effekseer;

/// <summary>Read-only adapter. No source document is ever opened for writing.</summary>
public sealed class EffekseerResourceParser : IResourceParser
{
    public const string UpstreamRevision = "82b37081a302b9f9eff0bf14dc6c845fca8c3c54";
    public bool CanRead(string path) => Path.GetExtension(path).ToLowerInvariant() is ".efkefc" or ".efkmat" or ".glb" or ".efkmodel";

    public Task<ParsedDocument> ParseAsync(string path, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        path = Path.GetFullPath(path);
        var format = "Unknown";
        int? version = null;
        try
        {
            if (WindowsPathResolver.HasReparsePoint(path))
                return Failure(path, format, version, "LinkedDocument", "Document contains a symlink/junction; target is not certified.");
            var length = new FileInfo(path).Length;
            if (length > 64 * 1024 * 1024) return Failure(path, format, version, "SizeLimit", "Inspector limit: 64 MiB per document.");
            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase)) { format = "GLB"; return ModelResourceReader.ReadGlb(path, data); }
            if (Path.GetExtension(path).Equals(".efkmodel", StringComparison.OrdinalIgnoreCase)) { format = "EFKMODEL"; return ModelResourceReader.ReadModel(path, data); }
            if (data.Length < 8) throw new InvalidDataException("Truncated file header.");
            format = Encoding.ASCII.GetString(data, 0, 4);
            version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            return format switch
            {
                "EFKE" when version == 0 => ReadEffect(path, data, version.Value),
                "EFKM" when version is 3 or 1610 or 1710 or 1800 => ReadMaterial(path, data, version.Value),
                _ => new ParsedDocument(path, format, version, null, null, ScanCoverage.Unsupported, false, [], [],
                    [new("UnsupportedFormat", $"Unsupported {format} version {version}; rewrite disabled.", true)])
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or JsonException or System.Xml.XmlException or OverflowException or DecoderFallbackException
            or KeyNotFoundException or InvalidOperationException)
        { return Failure(path, format, version, "ParseError", ex.Message); }
    }, cancellationToken);

    private static ParsedDocument Failure(string path, string format, int? version, string code, string message) =>
        new(path, format, version, null, null, ScanCoverage.Error, false, [], [], [new(code, message, true)]);

    private static ParsedDocument ReadEffect(string path, byte[] data, int version)
    {
        var chunks = ChunkContainer.Read(data, 8);
        var references = new List<ResourceReference>();
        var diagnostics = new List<Diagnostic>();
        var editor = EditorDataDecoder.Decode(ChunkContainer.RequiredSingle(chunks, "EDIT").Data);
        var editorVersion = editor.Descendants("ToolVersion").FirstOrDefault()?.Value;
        var info = ChunkContainer.RequiredSingle(chunks, "INFO");
        var reader = new BoundedBinaryReader(info.Data);
        var dependencyVersion = reader.Int32();
        if (!EffectFormatCompatibility.IsKnownInfo(dependencyVersion))
            return new(path, "EFKE", version, dependencyVersion, editorVersion, ScanCoverage.Unsupported, false,
                chunks.Select(c => c.Info with { Version = c.Info.Id == "INFO" ? dependencyVersion
                    : c.Info.Id == "BIN_" && c.Data.Length >= 8 && Encoding.ASCII.GetString(c.Data, 0, 4) == "SKFE"
                        ? BinaryPrimitives.ReadInt32LittleEndian(c.Data.AsSpan(4)) : null }).ToArray(), [],
                [new("UnsupportedDependencyVersion", $"INFO version {dependencyVersion} not verified.", true)], editor.ToString());
        if (dependencyVersion == 1610)
            ReadTables(reader, path, "INFO", ReferenceRole.Metadata,
                [ResourceType.Texture, ResourceType.Texture, ResourceType.Texture, ResourceType.Model,
                    ResourceType.Sound, ResourceType.Material, ResourceType.Curve], references);
        else
        {
            var count = reader.Count();
            for (var i = 0; i < count; i++)
            {
                var type = reader.Int32() switch
                { 0 => ResourceType.Effect, 1 => ResourceType.Texture, 2 => ResourceType.Sound,
                    3 => ResourceType.Model, 4 => ResourceType.Material, 5 => ResourceType.Curve, _ => ResourceType.Unknown };
                reader.Int32(); // Color-space flags, not the resource type.
                if (type == ResourceType.Unknown) diagnostics.Add(new("UnknownResourceType", $"INFO dependency {i} has an unknown resource type; rewrite disabled.", true));
                Add(references, path, $"INFO/dependencies/{i}", reader.Utf16Zero(), type, ReferenceRole.Metadata);
            }
        }
        reader.End();
        var runtimes = chunks.Where(c => c.Info.Id == "BIN_").ToArray();
        if (runtimes.Length == 0) throw new InvalidDataException("Missing BIN_ chunk.");
        var versions = new Dictionary<int, int>();
        var latestVersion = runtimes.Max(c => c.Data.Length >= 8 ? BinaryPrimitives.ReadInt32LittleEndian(c.Data.AsSpan(4)) : -1);
        for (var i = 0; i < runtimes.Length; i++)
        {
            var runtimeReader = new BoundedBinaryReader(runtimes[i].Data);
            if (Encoding.ASCII.GetString(runtimeReader.ReadBytes(4)) != "SKFE") throw new InvalidDataException("Invalid runtime signature.");
            var runtimeVersion = runtimeReader.Int32();
            versions[runtimes[i].Info.Offset] = runtimeVersion;
            if (!EffectFormatCompatibility.IsKnownRuntime(runtimeVersion))
            { diagnostics.Add(new("UnsupportedRuntime", $"BIN_[{i}] version {runtimeVersion} not verified.", true)); continue; }
            var types = new List<ResourceType> { ResourceType.Texture, ResourceType.Texture, ResourceType.Texture,
                ResourceType.Sound, ResourceType.Model, ResourceType.Material };
            if (runtimeVersion >= 1610) types.Add(ResourceType.Curve);
            ReadTables(runtimeReader, path, $"BIN_[{i}]/{runtimeVersion}",
                runtimeVersion == latestVersion ? ReferenceRole.Runtime : ReferenceRole.CompatibilityRuntime, types, references);
        }
        var infoSet = references.Where(r => r.Role == ReferenceRole.Metadata).Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtimeSet = references.Where(r => r.Role == ReferenceRole.Runtime).Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!infoSet.SetEquals(runtimeSet))
            diagnostics.Add(new("MetadataRuntimeMismatch", "INFO and newest BIN_ dependency tables disagree; both are shown.", true));
        foreach (var element in editor.Descendants().Where(e => !e.HasElements && LooksLikeFileReference(e.Value)))
            Add(references, path, "EDIT/" + Locator(element), element.Value, GuessType(element.Value), ReferenceRole.EditorCandidate);
        diagnostics.Add(new("ReadOnlyInvestigation", "Dependency tables and editor candidates inspected; node activation, material overrides and editor load are not certified. General rewrite disabled; the separate texture mapper validates supported copies."));
        foreach (var chunk in chunks.Where(c => c.Info.Id is not ("INFO" or "EDIT" or "BIN_")))
            diagnostics.Add(new("UnknownChunk", $"Unknown chunk {chunk.Info.Id}; preserved by read-only inspection."));
        return new(path, "EFKE", version, dependencyVersion, editorVersion, ScanCoverage.MetadataOnly, false,
            chunks.Select(c => c.Info with { Version = c.Info.Id == "INFO" ? dependencyVersion : versions.GetValueOrDefault(c.Info.Offset, -1) is var v && v >= 0 ? v : null }).ToArray(),
            references, diagnostics, editor.ToString());
    }

    private static ParsedDocument ReadMaterial(string path, byte[] data, int version)
    {
        var chunks = ChunkContainer.Read(data, 16);
        var references = new List<ResourceReference>();
        var prm = new BoundedBinaryReader(ChunkContainer.RequiredSingle(chunks, "PRM_").Data);
        for (var i = 0; i < 5; i++) prm.Int32();
        if (version >= 1710)
        {
            var requiredFunctions = prm.Count();
            for (var i = 0; i < requiredFunctions; i++) prm.Int32();
        }
        var count = prm.Count();
        for (var i = 0; i < count; i++)
        {
            prm.Utf8Zero(); // Display name
            prm.Utf8Zero(); // Uniform name (material version >= 3)
            Add(references, path, $"PRM_/textures/{i}/DefaultPath", prm.Utf8Zero(), ResourceType.Texture, ReferenceRole.MaterialDefault);
            for (var field = 0; field < 5; field++) prm.Int32();
        }
        // Remaining fields contain uniforms, not additional paths, for the verified material layouts.
        var jsonBytes = ChunkContainer.RequiredSingle(chunks, "DATA").Data;
        var jsonLength = jsonBytes.Length;
        if (jsonLength > 0 && jsonBytes[^1] == 0) jsonLength--;
        using var json = JsonDocument.Parse(jsonBytes.AsMemory(0, jsonLength), new() { MaxDepth = 128 });
        if (json.RootElement.GetProperty("Project").GetString() != "EffekseerMaterial") throw new InvalidDataException("Invalid material DATA project type.");
        if (json.RootElement.TryGetProperty("Textures", out var textures))
        {
            var i = 0;
            foreach (var texture in textures.EnumerateArray())
            {
                Add(references, path, $"DATA/Textures/{i++}/Path", texture.GetProperty("Path").GetString() ?? "", ResourceType.Texture, ReferenceRole.MaterialEditor);
            }
        }
        if (json.RootElement.TryGetProperty("Nodes", out var nodes))
        {
            var i = 0;
            foreach (var node in nodes.EnumerateArray())
            {
                var type = node.GetProperty("Type").GetString();
                var propIndex = type switch { "SampleTexture" or "TextureObject" => 0, "TextureObjectParameter" => 2, _ => -1 };
                if (propIndex >= 0 && node.TryGetProperty("Props", out var props) && props.GetArrayLength() > propIndex)
                    Add(references, path, $"DATA/Nodes/{i}/Props/{propIndex}/Value",
                        props[propIndex].GetProperty("Value").GetString() ?? "", ResourceType.Texture, ReferenceRole.MaterialEditor);
                i++;
            }
        }
        return new(path, "EFKM", version, null, null, ScanCoverage.MetadataOnly, false,
            chunks.Select(c => c.Info).ToArray(), references,
            [new("MaterialDefaults", "Default/editor texture paths shown conservatively. Effect parameter overrides can replace defaults; runtime necessity is not certified. Rewrite disabled.")]);
    }

    private static void ReadTables(BoundedBinaryReader reader, string owner, string prefix, ReferenceRole role,
        IEnumerable<ResourceType> types, List<ResourceReference> references)
    {
        var table = 0;
        foreach (var type in types)
        {
            var count = reader.Count();
            for (var i = 0; i < count; i++) Add(references, owner, $"{prefix}/table[{table}]/{i}", reader.Utf16Zero(), type, role);
            table++;
        }
    }

    private static void Add(List<ResourceReference> refs, string owner, string locator, string path, ResourceType type, ReferenceRole role)
    { if (!string.IsNullOrWhiteSpace(path)) refs.Add(new(owner, locator, path, type, role)); }
    private static string Identity(ResourceReference reference) => $"{reference.Type}|{reference.OriginalReference.Replace('\\', '/')}";
    private static string Locator(XElement element) => string.Join('/', element.AncestorsAndSelf().Reverse().Select(e =>
        $"{e.Name.LocalName}[{e.ElementsBeforeSelf(e.Name).Count()}]"));
    private static bool LooksLikeFileReference(string text) => text.Length < 4096 && !text.Contains('\n')
        && GuessType(text) != ResourceType.Unknown;
    private static ResourceType GuessType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".dds" or ".tga" or ".jpg" or ".jpeg" or ".bmp" or ".ktx" or ".exr" => ResourceType.Texture,
        ".efkmat" => ResourceType.Material, ".efkmodel" or ".glb" => ResourceType.Model,
        ".wav" or ".ogg" or ".mp3" => ResourceType.Sound, ".efkcurve" => ResourceType.Curve,
        ".efkefc" or ".efk" => ResourceType.Effect, _ => ResourceType.Unknown
    };
}

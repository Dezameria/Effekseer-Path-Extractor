namespace ResourceManager.Core.Models;

public enum TextureMappingMode { LinkExisting, CopyIntoProject }
public enum EffectWriteMode { NewCopy, ReplaceOriginal, SeparateFolder }
public sealed record ResourceCopy(string SourcePath, string SourceHash, string TargetPath);
public sealed record TextureRequest(string OriginalReference, string FileName, bool Required,
    IReadOnlyList<SearchCandidate> Candidates, ResourceType Type = ResourceType.Texture, string? OwnerPath = null)
{
    public string SelectionKey => OwnerPath is null ? OriginalReference : OwnerPath + "\n" + OriginalReference;
}
public sealed record SourceDocument(ParsedDocument Document, string Hash);
public sealed record ResourceDocumentEdit(string SourcePath, string SourceHash, string TargetPath,
    IReadOnlyDictionary<string, string> Mappings);
public sealed record TextureDiscovery(string EffectPath, string SearchRoot, string EffectHash,
    IReadOnlyList<TextureRequest> Requests, IReadOnlyList<Diagnostic> Diagnostics,
    string? EditorVersion = null, string? FormatProfile = null, IReadOnlyList<SourceDocument>? Documents = null);
public sealed record TextureSelection(string OriginalReference, string? CandidatePath, string DestinationName);
public sealed record TextureMappingItem(string OriginalReference, string SourcePath, string SourceHash,
    string TargetPath, string NewReference, bool CopyRequired, ResourceType Type = ResourceType.Texture, string? OwnerPath = null);
public sealed record TextureMappingPlan(string EffectPath, string EffectHash, string SearchRoot,
    string OutputEffectPath, TextureMappingMode Mode, string? TextureDirectory,
    IReadOnlyList<TextureMappingItem> Items, IReadOnlyList<Diagnostic> Diagnostics,
    EffectWriteMode WriteMode = EffectWriteMode.NewCopy, string? PackageRoot = null,
    string? BackupPath = null, string? JournalPath = null, IReadOnlyList<ResourceCopy>? AdditionalCopies = null,
    IReadOnlyList<ResourceDocumentEdit>? DocumentEdits = null, bool AllResources = false)
{
    public bool CanApply => Items.Count > 0 && !Diagnostics.Any(d => d.IsError);
}
public sealed record TextureMappingResult(string OutputEffectPath, string ManifestPath, int FilesCopied,
    int ReferencesMapped, AuditReport Audit, string? BackupPath = null);
public sealed record EffectDiscoveryCase(string EffectPath, TextureDiscovery? Discovery, string? Error);
public sealed record BatchDiscovery(string InputPath, string SearchRoot, IReadOnlyList<EffectDiscoveryCase> Effects,
    IReadOnlyList<Diagnostic> Diagnostics);

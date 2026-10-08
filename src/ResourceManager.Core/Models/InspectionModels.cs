namespace ResourceManager.Core.Models;

public enum ResourceType { Effect, Texture, Sound, Model, Material, Curve, Unknown }
public enum ReferenceRole { Metadata, Runtime, CompatibilityRuntime, EditorCandidate, MaterialDefault, MaterialEditor }
public enum ScanCoverage { Complete, MetadataOnly, Unsupported, Error }
public enum PortabilityOutcome { Pass, Fail, Inconclusive }

public sealed record Diagnostic(string Code, string Message, bool IsError = false);
public sealed record ChunkInfo(string Id, int Offset, int Size, int? Version = null);
public sealed record ResourceReference(string Owner, string Locator, string OriginalReference,
    ResourceType Type, ReferenceRole Role);

public sealed record ParsedDocument(string Path, string Format, int? Version, int? DependencyVersion,
    string? EditorVersion, ScanCoverage Coverage, bool CanRewrite,
    IReadOnlyList<ChunkInfo> Chunks, IReadOnlyList<ResourceReference> References,
    IReadOnlyList<Diagnostic> Diagnostics, string? EditorXml = null);

public sealed record PathAudit(string? ResolvedPath, bool Exists, bool InsideBoundary,
    bool IsAbsolute, string Status, string? Reason, bool HasLink = false, long? FileSize = null);
public sealed record AuditedReference(ResourceReference Reference, PathAudit Audit, int Depth);
public sealed record ResourceEntry(string Id, ResourceType Type, string Name, string? ResolvedPath,
    bool Exists, bool InsideBoundary, string Status, long? Size, int ReferenceCount,
    IReadOnlyList<AuditedReference> References);
public sealed record ScanProgress(string Task, int DocumentsCompleted, int DocumentsDiscovered);
public sealed record PortabilityResult(PortabilityOutcome Outcome, IReadOnlyList<string> Reasons);
public sealed record AuditReport(string Boundary, IReadOnlyList<string> EntryDocuments,
    IReadOnlyList<ParsedDocument> Documents, IReadOnlyList<ResourceEntry> Resources,
    IReadOnlyList<Diagnostic> Diagnostics, PortabilityResult Portability);
public sealed record SearchCandidate(string Path, long Size);
public sealed record MissingMatch(string ResourceId, IReadOnlyList<SearchCandidate> Candidates)
{
    public bool IsAmbiguous => Candidates.Count > 1;
}
public sealed record SearchResult(IReadOnlyList<MissingMatch> Matches, IReadOnlyList<Diagnostic> Diagnostics);

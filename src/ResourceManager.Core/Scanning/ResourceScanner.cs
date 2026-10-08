using ResourceManager.Core.Abstractions;
using ResourceManager.Core.Models;
using ResourceManager.Core.Validation;

namespace ResourceManager.Core.Scanning;

public sealed class ResourceScanner(IResourceParser parser, IPathResolver resolver)
{
    public async Task<AuditReport> ScanAsync(IEnumerable<string> entryDocuments, string boundary,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        boundary = Path.GetFullPath(boundary);
        var roots = entryDocuments.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var queue = new Queue<(string Path, int Depth)>(roots.Select(p => (p, 0)));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<ParsedDocument>();
        var references = new List<AuditedReference>();
        var diagnostics = new List<Diagnostic>();
        while (queue.TryDequeue(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(next.Path)) continue;
            progress?.Report(new("Inspecting " + Path.GetFileName(next.Path), documents.Count, visited.Count + queue.Count));
            var document = await parser.ParseAsync(next.Path, cancellationToken);
            documents.Add(document);
            diagnostics.AddRange(document.Diagnostics.Select(d => d with { Message = $"{Path.GetFileName(document.Path)}: {d.Message}" }));
            foreach (var reference in document.References)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var audit = resolver.Audit(reference.Owner, reference.OriginalReference, boundary);
                references.Add(new(reference, audit, next.Depth + 1));
                if (audit.Exists && !audit.HasLink && audit.ResolvedPath is { } target && parser.CanRead(target))
                    queue.Enqueue((target, next.Depth + 1));
            }
        }
        var resources = references.GroupBy(r => r.Audit.ResolvedPath ?? $"{r.Reference.Owner}|{r.Reference.OriginalReference}",
            StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var first = group.First();
            var items = group.ToArray();
            return new ResourceEntry(group.Key, first.Reference.Type,
                Path.GetFileName(first.Audit.ResolvedPath ?? first.Reference.OriginalReference),
                first.Audit.ResolvedPath, first.Audit.Exists, first.Audit.InsideBoundary,
                items.OrderByDescending(r => StatusPriority(r.Audit.Status)).First().Audit.Status, first.Audit.FileSize,
                items.Length, items);
        }).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        progress?.Report(new("Inspection complete", documents.Count, documents.Count));
        return new(boundary, roots, documents, resources, diagnostics,
            PortabilityValidator.Validate(documents, resources, diagnostics));
    }

    private static int StatusPriority(string status) => status switch
    { "ERROR" => 5, "UNRESOLVED" => 4, "MISSING" => 3, "EXTERNAL" => 2, _ => 1 };
}

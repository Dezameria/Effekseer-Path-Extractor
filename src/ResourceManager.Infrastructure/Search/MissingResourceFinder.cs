using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.FileSystem;

namespace ResourceManager.Infrastructure.Search;

public sealed class MissingResourceFinder
{
    public Task<SearchResult> FindAsync(IEnumerable<ResourceEntry> resources, string searchRoot,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var missing = resources.Where(r => r.Status == "MISSING").ToArray();
        var names = missing.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discovery = DocumentDiscovery.Enumerate(searchRoot, p => names.Contains(Path.GetFileName(p)), cancellationToken);
        var diagnostics = discovery.Diagnostics.ToList();
        var candidates = new List<SearchCandidate>();
        foreach (var path in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { candidates.Add(new(path, new FileInfo(path).Length)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { diagnostics.Add(new("CandidateAccess", $"{path}: {ex.Message}", true)); }
        }
        var index = candidates.ToLookup(c => Path.GetFileName(c.Path), StringComparer.OrdinalIgnoreCase);
        return new SearchResult(missing.Select(r => new MissingMatch(r.Id, index[r.Name].ToArray())).ToArray(), diagnostics);
    }, cancellationToken);
}

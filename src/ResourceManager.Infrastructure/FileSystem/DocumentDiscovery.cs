using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.FileSystem;

public sealed record DiscoveryResult(IReadOnlyList<string> Files, IReadOnlyList<Diagnostic> Diagnostics);

public static class DocumentDiscovery
{
    public static DiscoveryResult Enumerate(string root, Func<string, bool> include,
        CancellationToken cancellationToken = default, string? excludedRoot = null)
    {
        var files = new List<string>();
        var diagnostics = new List<Diagnostic>();
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(root));
        while (queue.TryDequeue(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (excludedRoot is not null && WindowsPathResolver.IsInside(directory, excludedRoot)) continue;
            try
            {
                if (WindowsPathResolver.HasReparsePoint(directory))
                {
                    diagnostics.Add(new("SkippedLink", $"Skipped directory containing a junction/symlink: {directory}", true));
                    continue;
                }
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attrs = File.GetAttributes(entry);
                        if (attrs.HasFlag(FileAttributes.ReparsePoint))
                        { diagnostics.Add(new("SkippedLink", $"Skipped link: {entry}", true)); continue; }
                        if (attrs.HasFlag(FileAttributes.Directory)) queue.Enqueue(entry);
                        else if (include(entry)) files.Add(entry);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { diagnostics.Add(new("EnumerationAccess", $"{entry}: {ex.Message}", true)); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { diagnostics.Add(new("EnumerationAccess", $"{directory}: {ex.Message}", true)); }
        }
        return new(files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(), diagnostics);
    }
}

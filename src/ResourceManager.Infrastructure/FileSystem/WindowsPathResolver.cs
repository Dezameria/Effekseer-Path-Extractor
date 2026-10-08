using ResourceManager.Core.Abstractions;
using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.FileSystem;

public sealed class WindowsPathResolver : IPathResolver
{
    public static bool IsInside(string path, string boundary)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundary));
        var full = Path.GetFullPath(path);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    public PathAudit Audit(string ownerDocument, string reference, string boundary)
    {
        string? path = null;
        var absolute = false;
        try
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Contains('\0'))
                return new(null, false, false, false, "UNRESOLVED", "Empty or invalid reference.");
            var normalized = reference.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            absolute = Path.IsPathFullyQualified(normalized);
            if (Path.IsPathRooted(normalized) && !absolute)
                return new(null, false, false, false, "UNRESOLVED", "Drive-relative/root-relative paths require an explicit resolution context.");
            path = absolute ? Path.GetFullPath(normalized) : Path.GetFullPath(normalized, Path.GetDirectoryName(Path.GetFullPath(ownerDocument))!);
            var inside = IsInside(path, boundary);
            var hasLink = HasReparsePoint(path);
            // GetAttributes distinguishes missing paths from access failures; File.Exists hides them.
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.Directory))
                return new(path, false, inside, absolute, "ERROR", "Reference points to a directory.", hasLink);
            if (hasLink)
                return new(path, true, false, absolute, "UNRESOLVED", "Symlink/junction target not certified by this inspector.", true);
            return new(path, true, inside, absolute, inside ? "VALID" : "EXTERNAL",
                absolute && inside ? "Absolute internal reference will remain bound to its original location after a move." : null,
                FileSize: new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return new(path, false, path is not null && IsInside(path, boundary), absolute, "MISSING", ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(path, false, false, absolute, "ERROR", ex.Message); }
    }

    public static bool HasReparsePoint(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try { if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true; }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        }
        return false;
    }
}

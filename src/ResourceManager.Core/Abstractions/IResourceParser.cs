using ResourceManager.Core.Models;

namespace ResourceManager.Core.Abstractions;

public interface IResourceParser
{
    bool CanRead(string path);
    Task<ParsedDocument> ParseAsync(string path, CancellationToken cancellationToken = default);
}

public interface IPathResolver
{
    PathAudit Audit(string ownerDocument, string reference, string boundary);
}

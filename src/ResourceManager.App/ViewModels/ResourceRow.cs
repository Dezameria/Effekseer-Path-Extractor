using ResourceManager.Core.Models;

namespace ResourceManager.App.ViewModels;

public sealed class ResourceRow(ResourceEntry resource)
{
    public ResourceEntry Resource { get; } = resource;
    public string Name => Resource.Name;
    public string Type => Resource.Type.ToString();
    public string Status => Resource.Status;
    public string ResolvedPath => Resource.ResolvedPath ?? "Unresolved";
    public int ReferenceCount => Resource.ReferenceCount;
    public string Scope => Resource.References.Any(r => r.Reference.Role is ReferenceRole.Runtime or ReferenceRole.CompatibilityRuntime or ReferenceRole.Metadata)
        ? "Runtime / ข้อมูล Effect" : "Editor / ค่า Material";
}

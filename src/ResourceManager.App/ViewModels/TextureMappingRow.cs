using ResourceManager.Core.Models;

namespace ResourceManager.App.ViewModels;

public sealed class TextureMappingRow : ObservableObject
{
    private TextureCandidateChoice? selected;
    private string destinationName;
    public TextureRequest Request { get; }
    public string OriginalReference => Request.OriginalReference;
    public string FileName => Request.FileName;
    public string ResourceKind => Path.GetExtension(FileName).Equals(".glb", StringComparison.OrdinalIgnoreCase) ? "GLB" : Request.Type.ToString();
    public string OwnerName => Request.OwnerPath is null ? "Effect" : Path.GetFileName(Request.OwnerPath);
    public bool IsTexture => Request.Type == ResourceType.Texture;
    public string Need => Request.Required ? "จำเป็น" : "เฉพาะ Editor";
    public IReadOnlyList<TextureCandidateChoice> Candidates { get; }
    public TextureCandidateChoice? SelectedCandidate { get => selected; set { if (Set(ref selected, value)) Changed(nameof(MatchStatus)); } }
    public string DestinationName { get => destinationName; set => Set(ref destinationName, value); }
    public string MatchStatus => selected is not null ? "พร้อม" : Candidates.Count > 1 ? "ชื่อซ้ำ · เลือกไฟล์" : Request.Required ? "ไม่พบไฟล์" : "ข้ามได้";
    public TextureMappingRow(TextureRequest request, string searchRoot)
    {
        Request = request;
        Candidates = request.Candidates.Select(c => new TextureCandidateChoice(c.Path, Path.GetRelativePath(searchRoot, c.Path))).ToArray();
        destinationName = request.FileName;
        if (Candidates.Count == 1) selected = Candidates[0];
    }
}

public sealed record TextureCandidateChoice(string Path, string DisplayPath);

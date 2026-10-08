using System.Collections.ObjectModel;
using ResourceManager.Core.Models;

namespace ResourceManager.App.ViewModels;

public sealed class BatchEffectRow : ObservableObject
{
    private bool included;
    private string status, outputPath = "", backupPath = "";
    public EffectDiscoveryCase Case { get; }
    public string Name { get; }
    public string EditorVersion => Case.Discovery?.EditorVersion ?? "—";
    public bool Included { get => included; set => Set(ref included, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string OutputPath { get => outputPath; set => Set(ref outputPath, value); }
    public string BackupPath { get => backupPath; set => Set(ref backupPath, value); }
    public ObservableCollection<TextureMappingRow> Textures { get; } = [];
    public string PendingStatus => Textures.Any(t => t.Candidates.Count > 1 && t.SelectedCandidate is null)
        ? $"ชื่อซ้ำ {Textures.Count(t => t.Candidates.Count > 1 && t.SelectedCandidate is null)} กรณี · เลือกไฟล์"
        : Textures.Any(t => t.Request.Required && t.SelectedCandidate is null) ? "ไม่พบ Asset ที่จำเป็น" : "รอตรวจตัวอย่าง";
    public TextureMappingPlan? Plan { get; set; }
    public BatchEffectRow(EffectDiscoveryCase item, string inputRoot)
    {
        Case = item;
        Name = Path.GetRelativePath(inputRoot, item.EffectPath);
        included = item.Discovery is not null;
        status = item.Error ?? "รอตรวจตัวอย่าง";
        if (item.Discovery is { } discovery)
        {
            foreach (var request in discovery.Requests) Textures.Add(new(request, discovery.SearchRoot));
            status = PendingStatus;
        }
    }
}

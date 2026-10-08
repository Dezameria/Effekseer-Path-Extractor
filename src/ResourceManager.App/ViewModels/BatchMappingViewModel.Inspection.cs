using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Core.Validation;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.FileSystem;
using ResourceManager.Infrastructure.Search;

namespace ResourceManager.App.ViewModels;

public sealed partial class BatchMappingViewModel
{
    private readonly EffekseerResourceParser inspectionParser = new();
    private string inspectionBoundary = "", inspectionFilter = "", inspectionStatus = "All";
    private string inspectionSummary = "ยังไม่ได้ตรวจไฟล์", inspectionDetails = "เลือกไฟล์ในผลตรวจเพื่อดู path ที่อ้างถึง", inspectionPortability = "";
    private bool inspectionExpanded;
    private ResourceRow? selectedInspectionResource;
    public AuditReport? InspectionReport { get; private set; }
    public ObservableCollection<ResourceRow> InspectionRows { get; } = [];
    public ICollectionView InspectionResources { get; private set; } = null!;
    public string[] InspectionStatuses { get; } = ["All", "VALID", "MISSING", "EXTERNAL", "UNRESOLVED", "ERROR"];
    public string InspectionBoundary { get => inspectionBoundary; set { if (Set(ref inspectionBoundary, value)) ClearInspection(); } }
    public string InspectionFilter { get => inspectionFilter; set { if (Set(ref inspectionFilter, value)) InspectionResources.Refresh(); } }
    public string InspectionStatus { get => inspectionStatus; set { if (Set(ref inspectionStatus, value)) InspectionResources.Refresh(); } }
    public string InspectionSummary { get => inspectionSummary; private set => Set(ref inspectionSummary, value); }
    public string InspectionDetails { get => inspectionDetails; private set => Set(ref inspectionDetails, value); }
    public string InspectionPortability { get => inspectionPortability; private set => Set(ref inspectionPortability, value); }
    public bool InspectionExpanded { get => inspectionExpanded; set => Set(ref inspectionExpanded, value); }
    public ResourceRow? SelectedInspectionResource { get => selectedInspectionResource; set { if (Set(ref selectedInspectionResource, value)) UpdateInspectionDetails(); } }
    public ICommand InspectCommand { get; private set; } = null!;
    public ICommand InspectionBoundaryCommand { get; private set; } = null!;
    public ICommand FindInspectionMissingCommand { get; private set; } = null!;

    private void InitializeInspection()
    {
        InspectionResources = CollectionViewSource.GetDefaultView(InspectionRows);
        InspectionResources.Filter = item => item is ResourceRow row && (InspectionStatus == "All" || row.Status == InspectionStatus)
            && (string.IsNullOrWhiteSpace(InspectionFilter) || row.Name.Contains(InspectionFilter, StringComparison.OrdinalIgnoreCase)
                || row.ResolvedPath.Contains(InspectionFilter, StringComparison.OrdinalIgnoreCase)
                || row.Resource.References.Any(r => r.Reference.OriginalReference.Contains(InspectionFilter, StringComparison.OrdinalIgnoreCase)));
        InspectCommand = new AsyncCommand(InspectAsync, () => IsIdle && (Directory.Exists(InputPath) || File.Exists(InputPath) && inspectionParser.CanRead(InputPath)));
        InspectionBoundaryCommand = new RelayCommand(() => Folder("เลือกขอบเขตสำหรับตรวจไฟล์", p => InspectionBoundary = p), () => IsIdle);
        FindInspectionMissingCommand = new AsyncCommand(FindInspectionMissingAsync, () => IsIdle && InspectionReport is not null && Directory.Exists(AssetRoot));
    }

    private void ResetInspectionInput()
    {
        inspectionBoundary = "";
        if (!string.IsNullOrWhiteSpace(InputPath))
            try { inspectionBoundary = Directory.Exists(InputPath) ? Path.GetFullPath(InputPath) : Path.GetDirectoryName(Path.GetFullPath(InputPath))!; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        Changed(nameof(InspectionBoundary));
        ClearInspection();
    }

    private void ClearInspection()
    {
        InspectionReport = null;
        InspectionRows.Clear();
        SelectedInspectionResource = null;
        InspectionSummary = "ยังไม่ได้ตรวจไฟล์";
        InspectionPortability = "";
        InspectionDetails = "เลือกตรวจสอบอย่างเดียวเพื่อดูไฟล์และ path โดยไม่บันทึกการแก้ไข";
    }

    public Task InspectAsync() => RunAsync(async token =>
    {
        ClearInspection();
        InspectionExpanded = true;
        var input = Path.GetFullPath(InputPath);
        var boundary = Path.GetFullPath(InspectionBoundary);
        if (!Directory.Exists(boundary)) throw new ArgumentException("เลือกขอบเขตผลตรวจที่มีอยู่จริง");
        var entries = Directory.Exists(input)
            ? await Task.Run(() => DocumentDiscovery.Enumerate(input, inspectionParser.CanRead, token), token)
            : File.Exists(input) && inspectionParser.CanRead(input) ? new DiscoveryResult([input], [])
            : throw new ArgumentException("เลือก Effect, Material, Model / GLB หรือโฟลเดอร์เพื่อตรวจไฟล์");
        var progress = new Progress<ScanProgress>(p => Message = $"กำลังตรวจ {p.DocumentsCompleted}/{p.DocumentsDiscovered} ไฟล์…");
        var report = await Task.Run(() => new ResourceScanner(inspectionParser, new WindowsPathResolver()).ScanAsync(entries.Files, boundary, progress, token), token);
        token.ThrowIfCancellationRequested();
        var diagnostics = report.Diagnostics.Concat(entries.Diagnostics).ToArray();
        SetInspectionReport(report with { Diagnostics = diagnostics, Portability = PortabilityValidator.Validate(report.Documents, report.Resources, diagnostics) });
        Message = "ตรวจสอบเสร็จแล้ว ดูรายละเอียดผลตรวจด้านล่างได้";
    });

    private void SetInspectionReport(AuditReport report)
    {
        InspectionReport = report;
        InspectionRows.Clear();
        foreach (var resource in report.Resources) InspectionRows.Add(new(resource));
        SelectedInspectionResource = null;
        InspectionSummary = $"ตรวจ {report.Documents.Count} ไฟล์ · Asset {report.Resources.Count} รายการ · พบ {report.Resources.Count(r => r.Exists)} · ไม่พบ {report.Resources.Count(r => !r.Exists)}";
        InspectionPortability = report.Portability.Outcome switch
        {
            PortabilityOutcome.Fail => "พบ path ที่หาย อยู่นอกขอบเขต หรือเป็น absolute จึงยังย้ายทั้งชุดไม่ได้",
            PortabilityOutcome.Inconclusive => "ตรวจ path และโครงสร้างแล้ว · ยังไม่รับรองการย้ายทั้งชุดหรือภาพใน Effekseer",
            _ => "path ที่ตรวจพบอยู่ภายในขอบเขตทั้งหมด"
        };
        InspectionDetails = "ขอบเขตผลตรวจ: " + report.Boundary + "\nตรวจอย่างเดียว ไม่เปลี่ยน path หรือคัดลอก Asset\n\n"
            + string.Join("\n", report.Diagnostics.Where(d => d.IsError).Select(d => d.Message));
    }

    private void UpdateInspectionDetails()
    {
        if (SelectedInspectionResource is not { } row) return;
        var text = new StringBuilder($"{row.Name} · {row.Type} · {row.Status}\nไฟล์จริง: {row.ResolvedPath}\nพบการอ้างถึง {row.ReferenceCount} จุด\n\n");
        foreach (var reference in row.Resource.References)
        {
            text.AppendLine("เจ้าของ: " + reference.Reference.Owner);
            text.AppendLine("path ในไฟล์: " + reference.Reference.OriginalReference);
            text.AppendLine("ตำแหน่งข้อมูล: " + reference.Reference.Locator + " · " + reference.Reference.Role);
            text.AppendLine("ผลตรวจ: " + reference.Audit.Status + (reference.Audit.IsAbsolute ? " · absolute" : ""));
            text.AppendLine();
        }
        InspectionDetails = text.ToString();
    }

    public Task FindInspectionMissingAsync() => RunAsync(async token =>
    {
        if (InspectionReport is not { } report) return;
        Message = "กำลังค้นไฟล์ที่หายในคลัง Asset…";
        var matches = await new MissingResourceFinder().FindAsync(report.Resources, AssetRoot, token);
        var text = new StringBuilder("ผลค้นจากคลัง: " + AssetRoot + "\nรายการนี้ใช้ตรวจอย่างเดียว ยังไม่ได้เลือกหรือ Map ไฟล์\n\n");
        foreach (var match in matches.Matches)
        {
            text.AppendLine(Path.GetFileName(match.ResourceId) + $" · พบ {match.Candidates.Count} ไฟล์" + (match.IsAmbiguous ? " · ชื่อซ้ำ" : ""));
            foreach (var candidate in match.Candidates) text.AppendLine(candidate.Path);
            text.AppendLine();
        }
        foreach (var diagnostic in matches.Diagnostics) text.AppendLine(diagnostic.Message);
        InspectionDetails = text.ToString();
        InspectionExpanded = true;
        Message = "ค้นเสร็จแล้ว ดูตำแหน่งไฟล์ในรายละเอียดผลตรวจได้";
    });
}

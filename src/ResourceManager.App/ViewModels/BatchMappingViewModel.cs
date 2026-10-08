using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.Mapping;
using ResourceManager.Infrastructure.Settings;

namespace ResourceManager.App.ViewModels;

public sealed partial class BatchMappingViewModel : ObservableObject
{
    private readonly ResourceMappingService resourceService = new();
    private readonly AssetLibraryStore store;
    private CancellationTokenSource? cancellation;
    private string inputPath = "", assetRoot = "", outputRoot = "", assetName = "", textureFolder = "Textures";
    private string message = "เลือก Effect หรือโฟลเดอร์ และเลือกคลัง Asset เพื่อเริ่ม", preview = "ยังไม่ได้ตรวจตัวอย่าง";
    private bool isBusy, replaceOriginal, allowRename, ready;
    private SavedAssetLibrary? selectedLibrary;
    private BatchEffectRow? selectedEffect;
    private BatchDiscovery? discovery;
    public ObservableCollection<SavedAssetLibrary> Libraries { get; } = [];
    public ObservableCollection<BatchEffectRow> Effects { get; } = [];
    public string InputPath { get => inputPath; set { if (Set(ref inputPath, value)) { ClearDiscovery(); ResetInspectionInput(); } } }
    public string AssetRoot { get => assetRoot; set { if (Set(ref assetRoot, value)) ClearDiscovery(); } }
    public string OutputRoot { get => outputRoot; set { if (Set(ref outputRoot, value)) { ClearDiscovery(); Changed(nameof(OutputEnabled)); } } }
    public string AssetName { get => assetName; set => Set(ref assetName, value); }
    public string TextureFolder { get => textureFolder; set { if (Set(ref textureFolder, value)) Invalidate(); } }
    public bool ReplaceOriginal { get => replaceOriginal; set { if (Set(ref replaceOriginal, value)) { Changed(nameof(CreateFolders)); Changed(nameof(CopyTextures)); Changed(nameof(LinkTextures)); Changed(nameof(KeepNames)); Changed(nameof(OutputEnabled)); Changed(nameof(ApplyLabel)); Invalidate(); } } }
    public bool CreateFolders { get => !ReplaceOriginal; set { if (value) ReplaceOriginal = false; } }
    public bool CopyTextures { get => !ReplaceOriginal; set => ReplaceOriginal = !value; }
    public bool LinkTextures { get => !CopyTextures; set { if (value) CopyTextures = false; } }
    public bool AllowRename { get => allowRename; set { if (Set(ref allowRename, value)) { Changed(nameof(KeepNames)); Invalidate(); } } }
    public bool KeepNames => !AllowRename || !CopyTextures;
    public bool IsBusy { get => isBusy; private set { Set(ref isBusy, value); Changed(nameof(IsIdle)); Changed(nameof(OutputEnabled)); CommandManager.InvalidateRequerySuggested(); } }
    public bool IsIdle => !IsBusy;
    public bool OutputEnabled => IsIdle && CreateFolders;
    public string Message { get => message; private set => Set(ref message, value); }
    public string Preview { get => preview; private set => Set(ref preview, value); }
    public string ApplyLabel => ReplaceOriginal ? "บันทึก path ใน Effect เดิม" : "สร้างชุดใหม่ให้ Effect ที่เลือก";
    public string Counts => $"Effect {Effects.Count} ไฟล์ · เลือก {Effects.Count(e => e.Included)} · กรณีชื่อซ้ำ {Effects.Sum(e => e.Textures.Count(t => t.Candidates.Count > 1))}";
    public SavedAssetLibrary? SelectedLibrary { get => selectedLibrary; set { if (Set(ref selectedLibrary, value) && value is not null) { AssetRoot = value.Path; AssetName = value.Name; } } }
    public BatchEffectRow? SelectedEffect { get => selectedEffect; set { if (Set(ref selectedEffect, value)) Changed(nameof(CurrentTextures)); } }
    public ObservableCollection<TextureMappingRow>? CurrentTextures => SelectedEffect?.Textures;
    public ICommand OpenEffectCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand AssetFolderCommand { get; }
    public ICommand OutputFolderCommand { get; }
    public ICommand SaveLibraryCommand { get; }
    public ICommand RemoveLibraryCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenResultCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    public BatchMappingViewModel(AssetLibraryStore? libraryStore = null)
    {
        store = libraryStore ?? new();
        InitializeInspection();
        OpenEffectCommand = new RelayCommand(() => { var dialog = new OpenFileDialog { Filter = "Effect และ Asset|*.efkefc;*.efkmat;*.efkmodel;*.glb", Title = "เลือก Effect เพื่อ Map หรือเลือก Asset เพื่อตรวจอย่างเดียว" }; if (dialog.ShowDialog() == true) SetInput(dialog.FileName); }, () => IsIdle);
        OpenFolderCommand = new RelayCommand(() => Folder("เลือกโฟลเดอร์ Effect (รวมโฟลเดอร์ย่อย)", SetInput), () => IsIdle);
        AssetFolderCommand = new RelayCommand(() => Folder("เลือกคลัง Asset", p => AssetRoot = p), () => IsIdle);
        OutputFolderCommand = new RelayCommand(() => Folder("เลือกโฟลเดอร์ผลลัพธ์ จะสร้างโฟลเดอร์แยกสำหรับแต่ละ Effect", p => OutputRoot = p), () => OutputEnabled);
        SaveLibraryCommand = new RelayCommand(SaveLibrary, () => IsIdle && Directory.Exists(AssetRoot));
        RemoveLibraryCommand = new RelayCommand(RemoveLibrary, () => IsIdle && SelectedLibrary is not null);
        FindCommand = new AsyncCommand(FindAsync, () => IsIdle && (Directory.Exists(InputPath) || File.Exists(InputPath) && InputPath.EndsWith(".efkefc", StringComparison.OrdinalIgnoreCase)) && Directory.Exists(AssetRoot));
        PreviewCommand = new AsyncCommand(PreviewAsync, () => IsIdle && discovery is not null && Effects.Any(e => e.Included));
        ApplyCommand = new AsyncCommand(ApplyAsync, () => IsIdle && ready);
        CancelCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy);
        SelectAllCommand = new RelayCommand(() => { foreach (var row in Effects.Where(e => e.Case.Discovery is not null)) row.Included = true; }, () => IsIdle);
        SelectNoneCommand = new RelayCommand(() => { foreach (var row in Effects) row.Included = false; }, () => IsIdle);
        OpenResultCommand = new RelayCommand(() =>
        {
            var path = SelectedEffect?.OutputPath;
            if (path is null) return;
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); System.Diagnostics.Process.Start(start);
        }, () => IsIdle && File.Exists(SelectedEffect?.OutputPath));
        try
        {
            var settings = store.Load();
            foreach (var library in settings.Libraries) Libraries.Add(library);
            outputRoot = settings.OutputRoot ?? "";
            SelectedLibrary = Libraries.FirstOrDefault(l => l.Path == settings.SelectedPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { Message = "อ่านคลังที่บันทึกไม่ได้: " + ex.Message; }
    }

    public void SetInput(string path)
    {
        if (IsBusy) return;
        InputPath = Path.GetFullPath(path);
        if (OutputRoot.Length == 0) OutputRoot = Path.Combine(Directory.Exists(InputPath) ? InputPath : Path.GetDirectoryName(InputPath)!, "MappedEffects");
    }

    public Task FindAsync() => RunAsync(async token =>
    {
        ClearDiscovery();
        var progress = new Progress<string>(p => Message = p);
        var excluded = CreateFolders && OutputRoot.Length > 0 ? Path.GetFullPath(OutputRoot) : null;
        var result = await resourceService.DiscoverBatchAsync(InputPath, AssetRoot, excluded, progress, token);
        discovery = result;
        var root = Directory.Exists(result.InputPath) ? result.InputPath : Path.GetDirectoryName(result.InputPath)!;
        foreach (var item in result.Effects)
        {
            var row = new BatchEffectRow(item, root);
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BatchEffectRow.Included)) { Invalidate(); Changed(nameof(Counts)); } };
            foreach (var texture in row.Textures) texture.PropertyChanged += (_, _) => Invalidate();
            Effects.Add(row);
        }
        SelectedEffect = Effects.FirstOrDefault();
        Changed(nameof(Counts));
        var duplicates = Effects.Sum(e => e.Textures.Count(t => t.Candidates.Count > 1));
        Message = duplicates > 0 ? $"พบชื่อ Asset ซ้ำ {duplicates} กรณี เลือก Effect แล้วเลือกไฟล์จาก path ในรายการ ก่อนตรวจตัวอย่าง" : "ค้นเสร็จแล้ว เลือก Effect ที่ต้องการและตรวจตัวอย่าง";
        Preview = result.Diagnostics.Count > 0 ? string.Join("\n", result.Diagnostics.Select(d => d.Message)) : "ค้น Asset ทุกชนิดจาก path ใน Effect และ Texture ภายใน Material · ใช้ไฟล์ที่มีอยู่ หรือสร้างชุดใหม่ได้หลังค้น · รายการเฉพาะ Editor ที่หาไม่เจอจะแจ้งเตือน";
    });

    public Task PreviewAsync() => RunAsync(async token =>
    {
        ready = false;
        if (discovery is null) return;
        if (!ReplaceOriginal && string.IsNullOrWhiteSpace(OutputRoot)) throw new ArgumentException("เลือกโฟลเดอร์ผลลัพธ์ก่อน");
        var text = new StringBuilder();
        if (ReplaceOriginal) text.AppendLine("ใช้ Asset ที่มีอยู่ทุกชนิด · แก้ path ใน Effect เดิม พร้อมสำรอง .bak · ไม่สร้างโฟลเดอร์ Asset\n");
        else text.AppendLine("สร้างชุดใหม่แยกต่อ Effect · คัดลอก Asset และแก้ path ในสำเนา Material\n");
        foreach (var row in Effects.Where(e => e.Included))
        {
            token.ThrowIfCancellationRequested();
            row.Plan = null;
            try
            {
                if (row.Case.Discovery is null) throw new InvalidDataException(row.Case.Error);
                var selections = row.Textures.Select(t => new TextureSelection(t.Request.SelectionKey, t.SelectedCandidate?.Path,
                    CopyTextures && AllowRename && t.Request.Type == ResourceType.Texture ? t.DestinationName : ""));
                var plan = await resourceService.PlanAdvancedAsync(row.Case.Discovery, selections,
                    CopyTextures ? TextureMappingMode.CopyIntoProject : TextureMappingMode.LinkExisting, TextureFolder,
                    ReplaceOriginal ? EffectWriteMode.ReplaceOriginal : EffectWriteMode.SeparateFolder, InputPath, OutputRoot, token);
                row.Plan = plan;
                row.OutputPath = plan.OutputEffectPath;
                row.BackupPath = plan.BackupPath ?? "";
                row.Status = plan.CanApply ? "พร้อมทำงาน" : "ต้องแก้รายการ Asset / ปลายทาง";
                text.AppendLine($"{row.Name} · Effekseer {row.EditorVersion} (กลุ่ม {row.Case.Discovery.FormatProfile})\nปลายทาง: {row.OutputPath}");
                if (row.BackupPath.Length > 0) text.AppendLine("สำรอง: " + row.BackupPath);
                text.AppendLine($"Map path ใน Effect {plan.Items.Count(i => i.OwnerPath is null)} รายการ · ตรวจ path ภายใน Material {plan.Items.Count(i => i.OwnerPath is not null)} รายการ");
                if (plan.Mode == TextureMappingMode.CopyIntoProject) text.AppendLine($"แก้สำเนา Material {plan.DocumentEdits?.Count ?? 0} ไฟล์ · ที่เก็บ Asset: {plan.PackageRoot}");
                else text.AppendLine("Asset ทุกชนิดใช้ไฟล์เดิมจากคลัง: " + plan.SearchRoot);
                foreach (var d in plan.Diagnostics) text.AppendLine((d.IsError ? "ต้องแก้: " : "หมายเหตุ: ") + d.Message);
                text.AppendLine();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { row.Status = ex.Message; text.AppendLine(row.Name + ": " + ex.Message); }
        }
        var chosen = Effects.Where(e => e.Included).ToArray();
        var plans = chosen.Where(e => e.Plan?.CanApply == true).Select(e => e.Plan!).ToArray();
        var conflicts = TextureMappingService.ValidateBatchPlans(plans);
        foreach (var conflict in conflicts) text.AppendLine(conflict.Message);
        ready = chosen.Length > 0 && chosen.All(e => e.Plan?.CanApply == true) && conflicts.Count == 0 && !discovery.Diagnostics.Any(d => d.IsError);
        Preview = text.ToString();
        Message = ready ? $"พร้อมทำงาน {chosen.Length} Effect ตรวจ path แล้วกดปุ่มบันทึก" : "ยังทำงานไม่ได้ แก้กรณีที่เตือน หรือเอาเครื่องหมายเลือกออกจาก Effect ที่ยังไม่พร้อม แล้วตรวจตัวอย่างใหม่";
        SaveSettings();
    });

    public Task ApplyAsync() => RunAsync(async token =>
    {
        if (!ready) return;
        ready = false;
        var successes = 0;
        var failures = 0;
        var text = new StringBuilder();
        var chosen = Effects.Where(e => e.Included).ToArray();
        foreach (var row in chosen)
        {
            token.ThrowIfCancellationRequested();
            row.Status = "กำลังทำงาน";
            Message = $"กำลังทำ {row.Name} ({successes + failures + 1}/{chosen.Length})";
            try
            {
                var plan = row.Plan ?? throw new InvalidOperationException("ตรวจตัวอย่างใหม่");
                var result = await resourceService.ApplyAsync(plan, token: token);
                row.Status = "สำเร็จ";
                row.OutputPath = result.OutputEffectPath;
                row.BackupPath = result.BackupPath ?? "";
                SetInspectionReport(result.Audit);
                successes++;
                text.AppendLine($"สำเร็จ: {row.OutputPath}\nบันทึกการ Map: {result.ManifestPath}");
                if (result.BackupPath is not null) text.AppendLine("สำรองต้นฉบับ: " + result.BackupPath);
                if (plan.AllResources) text.AppendLine($"คัดลอก Asset {result.FilesCopied} ไฟล์ · Map {result.ReferencesMapped} path · ทรัพยากรที่ยังหาไม่เจอ {result.Audit.Resources.Count(r => !r.Exists)} รายการ");
                foreach (var resource in result.Audit.Resources.Where(r => !r.Exists)) text.AppendLine($"ยังไม่พบ: {resource.Name} · {resource.ResolvedPath}");
                if (plan.Mode == TextureMappingMode.LinkExisting) text.AppendLine("ใช้คลังเดิม: path นอกโฟลเดอร์ Effect เป็นไปตามโหมดนี้ ต้องเก็บคลังไว้ที่เดิม");
                foreach (var diagnostic in result.Audit.Diagnostics.Where(d => d.IsError)) text.AppendLine("ผลตรวจ: " + diagnostic.Message);
                var limited = result.Audit.Documents.Count(d => d.Coverage != ScanCoverage.Complete);
                if (limited > 0) text.AppendLine($"ตรวจโครงสร้างและ path แล้ว {result.Audit.Documents.Count} ไฟล์ · ยังไม่ได้ยืนยันการแสดงผลจริงใน Effekseer");
                text.AppendLine();
            }
            catch (OperationCanceledException) { row.Status = "ยกเลิก / ย้อนงานปัจจุบัน"; throw; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { row.Status = "ไม่สำเร็จ: " + ex.Message; text.AppendLine(row.Name + ": " + row.Status); failures++; }
            finally { row.Plan = null; Preview = text.ToString(); }
        }
        Message = $"เสร็จแล้ว สำเร็จ {successes} / {chosen.Length} · ไม่สำเร็จ {failures} · เลือกแถวแล้วเปิดตำแหน่งผลลัพธ์ได้";
    });

    private void Invalidate()
    {
        ready = false;
        foreach (var row in Effects)
        {
            row.Plan = null;
            row.OutputPath = ""; row.BackupPath = "";
            if (row.Case.Discovery is not null) row.Status = row.PendingStatus;
        }
        Preview = "รายการหรือการตั้งค่าเปลี่ยนแล้ว ตรวจตัวอย่างใหม่ก่อนบันทึก";
        CommandManager.InvalidateRequerySuggested();
    }
    private void ClearDiscovery() { discovery = null; Invalidate(); Effects.Clear(); SelectedEffect = null; Changed(nameof(Counts)); }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        cancellation = new(); IsBusy = true;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { ready = false; Message = "ยกเลิกแล้ว Effect ที่ทำสำเร็จก่อนหน้าเก็บไว้ งานปัจจุบันย้อนกลับเมื่อทำได้ ดูผลรายไฟล์"; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { ready = false; Message = "ทำรายการไม่สำเร็จ: " + ex.Message; }
        finally { cancellation.Dispose(); cancellation = null; IsBusy = false; }
    }
    private void SaveLibrary()
    {
        try
        {
            var path = Path.GetFullPath(AssetRoot);
            var library = new SavedAssetLibrary(string.IsNullOrWhiteSpace(AssetName) ? new DirectoryInfo(path).Name : AssetName.Trim(), path);
            var previous = Libraries.FirstOrDefault(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (previous is not null) Libraries.Remove(previous);
            Libraries.Add(library); SelectedLibrary = library; SaveSettings();
            Message = "บันทึกคลัง Asset แล้ว ครั้งหน้าเลือกจากรายการได้";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { Message = "บันทึกคลังไม่ได้: " + ex.Message; }
    }
    private void RemoveLibrary()
    {
        try { if (SelectedLibrary is { } library) { Libraries.Remove(library); SelectedLibrary = null; SaveSettings(); Message = "เอาคลังออกจากรายการแล้ว ไฟล์ Asset ยังอยู่ครบ"; } }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { Message = "บันทึกการเอาออกไม่ได้: " + ex.Message; }
    }
    private void SaveSettings() => store.Save(new(1, Libraries.ToArray(), AssetRoot, OutputRoot));
    private static void Folder(string title, Action<string> accept) { var dialog = new OpenFolderDialog { Title = title }; if (dialog.ShowDialog() == true) accept(dialog.FolderName); }
}

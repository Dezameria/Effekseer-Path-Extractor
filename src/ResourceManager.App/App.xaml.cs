using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ResourceManager.App.ViewModels;
using ResourceManager.Infrastructure.FileSystem;
using ResourceManager.Infrastructure.Settings;

namespace ResourceManager.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smoke = e.Args.Length >= 3 && e.Args[0].StartsWith("--", StringComparison.Ordinal) && e.Args[0].EndsWith("smoke-test", StringComparison.Ordinal);
        using var bindingLog = new StringWriter();
        using var bindingListener = new TextWriterTraceListener(bindingLog);
        if (smoke) PresentationTraceSources.DataBindingSource.Listeners.Add(bindingListener);
        var viewModel = smoke ? new BatchMappingViewModel(new AssetLibraryStore(Path.GetFullPath(e.Args[^1]) + ".settings.json")) : new BatchMappingViewModel();
        var window = new MainWindow(viewModel);
        MainWindow = window;
        if (smoke)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var empty = e.Args[0] is "--empty-smoke-test" or "--batch-empty-smoke-test";
                var inspection = e.Args[0] is "--smoke-test" or "--inspection-search-smoke-test";
                var resources = e.Args[0].StartsWith("--resources-", StringComparison.Ordinal);
                var input = e.Args[1]; var snapshot = Path.GetFullPath(e.Args[^1]);
                if (e.Args[0] is "--batch-small-smoke-test" or "--resources-small-smoke-test") { window.Width = 960; window.Height = 720; }
                if (!empty) viewModel.SetInput(input);
                if (inspection)
                {
                    if (e.Args[0] == "--inspection-search-smoke-test")
                    {
                        if (e.Args.Length != 4) throw new ArgumentException("Inspection search smoke: input, library, snapshot.");
                        viewModel.AssetRoot = e.Args[2];
                    }
                    await viewModel.InspectAsync();
                    if (viewModel.InspectionReport is not { Documents.Count: > 0 } report || report.Diagnostics.Any(d => d.IsError))
                        throw new InvalidOperationException("Read-only inspection failed: " + viewModel.Message);
                    if (viewModel.ApplyCommand.CanExecute(null)) throw new InvalidOperationException("Read-only inspection must never enable writes.");
                    viewModel.InspectionStatus = "MISSING";
                    if (viewModel.InspectionResources.Cast<ResourceRow>().Any(r => r.Status != "MISSING")) throw new InvalidOperationException("Status filter failed.");
                    viewModel.InspectionStatus = "All";
                    var row = viewModel.InspectionRows.FirstOrDefault();
                    if (row is not null)
                    {
                        viewModel.InspectionFilter = row.Name;
                        if (!viewModel.InspectionResources.Cast<ResourceRow>().Contains(row)) throw new InvalidOperationException("Name filter failed.");
                        viewModel.InspectionFilter = "";
                        viewModel.SelectedInspectionResource = row;
                        if (!viewModel.InspectionDetails.Contains(row.Resource.References[0].Reference.OriginalReference, StringComparison.Ordinal))
                            throw new InvalidOperationException("Reference details failed.");
                    }
                    if (e.Args[0] == "--inspection-search-smoke-test") await viewModel.FindInspectionMissingAsync();
                }
                else if (!empty)
                {
                    if (e.Args.Length != 5) throw new ArgumentException("Mapping smoke: input, library, output, snapshot.");
                    viewModel.AssetRoot = e.Args[2]; viewModel.OutputRoot = e.Args[3]; viewModel.AssetName = "Smoke library";
                    viewModel.SaveLibraryCommand.Execute(null);
                    var reloaded = new BatchMappingViewModel(new AssetLibraryStore(snapshot + ".settings.json"));
                    if (reloaded.AssetRoot != Path.GetFullPath(e.Args[2])) throw new InvalidOperationException("Saved library did not reload.");
                    viewModel.ReplaceOriginal = e.Args[0] is "--batch-inplace-smoke-test" or "--resources-inplace-smoke-test";
                    await viewModel.FindAsync();
                    if (viewModel.Effects.Count == 0) throw new InvalidOperationException("Mapping scan failed: " + viewModel.Message);
                    if (resources && (!viewModel.Effects.SelectMany(r => r.Textures).Any(t => t.Request.Type == ResourceManager.Core.Models.ResourceType.Material)
                        || !viewModel.Effects.SelectMany(r => r.Textures).Any(t => t.Request.OriginalReference.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidOperationException("Discovery must include materials and GLB.");
                    await viewModel.PreviewAsync();
                    if (viewModel.ReplaceOriginal && viewModel.Effects.Where(r => r.Plan is not null).Any(r => r.Plan!.Items.Any(i => i.CopyRequired)
                        || r.Plan!.TextureDirectory is not null || r.Plan!.DocumentEdits?.Count > 0))
                        throw new InvalidOperationException("Existing mode must never copy assets or edit materials.");
                    if (e.Args[0] is "--batch-resolved-smoke-test" or "--resources-resolved-smoke-test")
                    {
                        if (viewModel.ApplyCommand.CanExecute(null)) throw new InvalidOperationException("Expected unresolved duplicates.");
                        foreach (var row in viewModel.Effects.SelectMany(r => r.Textures).Where(t => t.Candidates.Count > 1))
                        { row.SelectedCandidate = row.Candidates[^1]; row.DestinationName = "chosen-" + row.FileName; }
                        await viewModel.PreviewAsync();
                        if (!viewModel.ApplyCommand.CanExecute(null) || viewModel.Effects.Where(r => r.Included).SelectMany(r => r.Plan!.Items).Any(i => Path.GetFileName(i.TargetPath) != Path.GetFileName(i.SourcePath)))
                            throw new InvalidOperationException("Renaming must be off by default.");
                        viewModel.AllowRename = true;
                        if (viewModel.ApplyCommand.CanExecute(null)) throw new InvalidOperationException("Rename changes must invalidate preview.");
                        await viewModel.PreviewAsync();
                        if (!viewModel.ApplyCommand.CanExecute(null) || !viewModel.Effects.Where(r => r.Included).SelectMany(r => r.Plan!.Items).Any(i => Path.GetFileName(i.TargetPath).StartsWith("chosen-", StringComparison.Ordinal)))
                            throw new InvalidOperationException("Optional rename failed.");
                    }
                    if (e.Args[0] is "--batch-conflict-smoke-test" or "--resources-conflict-smoke-test")
                    {
                        if (viewModel.ApplyCommand.CanExecute(null) || !viewModel.Effects.SelectMany(r => r.Textures).Any(t => t.Candidates.Count > 1))
                            throw new InvalidOperationException("Duplicate cases must block until selected.");
                    }
                    else
                    {
                        if (!viewModel.ApplyCommand.CanExecute(null)) throw new InvalidOperationException("Preview failed: " + viewModel.Preview);
                        if (e.Args[0] is "--batch-smoke-test" or "--batch-inplace-smoke-test" or "--batch-resolved-smoke-test" or "--resources-smoke-test" or "--resources-inplace-smoke-test" or "--resources-resolved-smoke-test")
                        {
                            await viewModel.ApplyAsync();
                            if (viewModel.Effects.Any(r => r.Included && r.Status != "สำเร็จ")) throw new InvalidOperationException("Mapping failed: " + viewModel.Preview);
                            if (viewModel.InspectionReport is null) throw new InvalidOperationException("Mapping must supply the detailed inspection report.");
                        }
                    }
                }
                var view = (BatchMappingView)window.FindName("BatchView");
                if (!empty && e.Args[0] != "--resources-top-smoke-test") ((ScrollViewer)view.FindName("BatchScroll")).ScrollToBottom();
                var boundary = Directory.Exists(input) ? Path.GetFullPath(input) : Path.GetDirectoryName(Path.GetFullPath(input))!;
                if (WindowsPathResolver.IsInside(snapshot, boundary) || WindowsPathResolver.HasReparsePoint(snapshot)) throw new ArgumentException("Snapshot must be outside the input scope and links.");
                var content = (FrameworkElement)window.Content; window.Content = null; content.DataContext = viewModel; content.Resources = window.Resources;
                content.Measure(new Size(window.Width, window.Height)); content.Arrange(new Rect(0, 0, window.Width, window.Height)); content.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                bindingListener.Flush();
                if (bindingLog.ToString().Contains("Error:", StringComparison.Ordinal)) throw new InvalidOperationException("WPF binding failed: " + bindingLog);
                var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                using var stream = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write); encoder.Save(stream);
                Shutdown(0);
            }
            catch (Exception ex) { Trace.WriteLine(ex); Console.Error.WriteLine(ex); Shutdown(1); }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingListener); }
            return;
        }
        window.Show();
        if (e.Args.Length == 1)
        {
            try
            {
                viewModel.SetInput(e.Args[0]);
                if (Path.GetExtension(e.Args[0]).ToLowerInvariant() is ".efkmat" or ".efkmodel" or ".glb") await viewModel.InspectAsync();
            }
            catch (Exception ex) when (ex is ArgumentException or IOException) { MessageBox.Show(ex.Message, "เปิดไฟล์ไม่ได้"); }
        }
    }
}
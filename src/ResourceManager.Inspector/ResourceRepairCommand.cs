using System.Text.Json;
using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.Mapping;

internal static class ResourceRepairCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("repair <effect.efkefc> --resources <folder> [--output-root <folder> | --in-place] [--mode copy|link] [--folder Textures] [--choices <json>] [--export-choices <new.json>] [--apply]\nAlways discovers every effect asset and nested material textures. --in-place links existing assets without copying them; --output-root creates a new package with rewritten material copies. Mode defaults to link for --in-place and copy otherwise; other combinations are rejected. Existing materials with broken texture paths require repair or a new package. Defaults to preview. GLB must be embedded GLB 2; efkmodel layout 6. Duplicate cases require explicit choices.");
            return 0;
        }
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        try
        {
            var options = new Dictionary<string, string>(); var apply = false; var inPlace = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--apply") { apply = true; continue; }
                if (args[i] == "--in-place") { inPlace = true; continue; }
                if (args[i] is not ("--resources" or "--output-root" or "--mode" or "--folder" or "--choices" or "--export-choices") || i + 1 >= args.Length) throw new ArgumentException("Unknown/incomplete option: " + args[i]);
                if (!options.TryAdd(args[i], args[++i])) throw new ArgumentException("Duplicate option.");
            }
            if (!options.TryGetValue("--resources", out var library)) throw new ArgumentException("Specify --resources.");
            if (inPlace && options.ContainsKey("--output-root")) throw new ArgumentException("Choose --in-place or --output-root.");
            var mode = options.GetValueOrDefault("--mode", inPlace ? "link" : "copy") switch { "copy" => TextureMappingMode.CopyIntoProject, "link" => TextureMappingMode.LinkExisting, _ => throw new ArgumentException("Mode must be copy or link.") };
            var service = new ResourceMappingService(); var discovery = await service.DiscoverAsync(args[0], library, cancel.Token);
            var choices = discovery.Requests.Select(r => new TextureSelection(r.SelectionKey, r.Candidates.Count == 1 ? r.Candidates[0].Path : null, "")).ToArray();
            if (options.TryGetValue("--export-choices", out var export))
            {
                var path = Path.GetFullPath(export);
                if (ResourceManager.Infrastructure.FileSystem.WindowsPathResolver.HasReparsePoint(path)) throw new ArgumentException("Choices output contains a link.");
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                await JsonSerializer.SerializeAsync(file, choices, new JsonSerializerOptions { WriteIndented = true }, cancel.Token);
            }
            if (options.TryGetValue("--choices", out var choiceFile)) choices = JsonSerializer.Deserialize<TextureSelection[]>(await File.ReadAllTextAsync(choiceFile, cancel.Token)) ?? throw new ArgumentException("Invalid choices JSON.");
            var outputRoot = options.GetValueOrDefault("--output-root", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "MappedEffects"));
            var plan = await service.PlanAdvancedAsync(discovery, choices, mode, options.GetValueOrDefault("--folder", "Textures"),
                inPlace ? EffectWriteMode.ReplaceOriginal : EffectWriteMode.SeparateFolder, args[0], outputRoot, cancel.Token);
            Console.WriteLine($"RESOURCE REPAIR PREVIEW | Effekseer {discovery.EditorVersion}\nOutput: {plan.OutputEffectPath}\nMaterial copies rewritten: {plan.DocumentEdits?.Count}\nAsset references: {plan.Items.Count}");
            foreach (var request in discovery.Requests) Console.WriteLine($"{request.Type} | {request.FileName} | {(request.OwnerPath is null ? "Effect" : Path.GetFileName(request.OwnerPath))} | {(request.Required ? "Required" : "Editor only")} | {request.Candidates.Count} candidate(s)");
            foreach (var diagnostic in plan.Diagnostics) Console.WriteLine((diagnostic.IsError ? "BLOCKED: " : "NOTE: ") + diagnostic.Message);
            if (!plan.CanApply) return 1;
            if (!apply) { Console.WriteLine("Preview only. Add --apply to save."); return 0; }
            var result = await service.ApplyAsync(plan, token: cancel.Token);
            Console.WriteLine($"Saved: {result.OutputEffectPath}\nBackup: {result.BackupPath}\nJournal: {result.ManifestPath}\nAssets copied: {result.FilesCopied}\nUnresolved resources: {result.Audit.Resources.Count(r => !r.Exists)}\nPortability within effect folder: {result.Audit.Portability.Outcome} (linked library assets may be outside this folder; rendering is not certified)");
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Repair cancelled."); return 130; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        { Console.Error.WriteLine(ex.Message); return 2; }
    }
}

using ResourceManager.Core.Models;
using ResourceManager.Infrastructure.Mapping;

internal static class MappingCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("map <effect.efkefc> --resources <folder> [--mode link|copy] [--folder Textures] [--output-name name.mapped.efkefc] [--apply]\nDefaults to preview. Duplicate filenames must be selected in the desktop UI.");
            return 0;
        }
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        try
        {
            var options = new Dictionary<string, string>();
            var apply = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--apply") { apply = true; continue; }
                if (args[i] is not ("--resources" or "--mode" or "--folder" or "--output-name") || i + 1 >= args.Length)
                    throw new ArgumentException("Unknown or incomplete option: " + args[i]);
                if (!options.TryAdd(args[i], args[++i])) throw new ArgumentException("Duplicate option.");
            }
            if (!options.TryGetValue("--resources", out var root)) throw new ArgumentException("Specify --resources <folder>.");
            var mode = options.GetValueOrDefault("--mode", "copy") switch
            { "copy" => TextureMappingMode.CopyIntoProject, "link" => TextureMappingMode.LinkExisting, _ => throw new ArgumentException("Mode must be link or copy.") };
            var service = new TextureMappingService();
            var discovery = await service.DiscoverAsync(args[0], root, cancellation.Token);
            var selections = discovery.Requests.Select(r => new TextureSelection(r.OriginalReference,
                r.Candidates.Count == 1 ? r.Candidates[0].Path : null, r.FileName));
            var plan = await service.PlanAsync(discovery, selections, mode, options.GetValueOrDefault("--folder", "Textures"),
                options.GetValueOrDefault("--output-name", Path.GetFileNameWithoutExtension(args[0]) + ".mapped.efkefc"), cancellation.Token);
            Console.WriteLine($"MAPPING PREVIEW: {mode}\nEffekseer: {discovery.EditorVersion} (profile {discovery.FormatProfile})\nOutput: {plan.OutputEffectPath}\nOriginal stays unchanged.");
            foreach (var item in plan.Items) Console.WriteLine($"{item.OriginalReference}\n  -> {item.NewReference}\n  {(item.CopyRequired ? "Copy" : "Link/reuse")}: {item.SourcePath}");
            foreach (var d in plan.Diagnostics) Console.WriteLine($"{(d.IsError ? "BLOCKED" : "NOTE")}: {d.Message}");
            if (!plan.CanApply) return 1;
            if (!apply) { Console.WriteLine("Preview only. Add --apply to create the mapped copy."); return 0; }
            var result = await service.ApplyAsync(plan, token: cancellation.Token);
            Console.WriteLine($"Created: {result.OutputEffectPath}\nTextures copied: {result.FilesCopied}\nPaths mapped: {result.ReferencesMapped}\nFull portability: {result.Audit.Portability.Outcome}");
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Mapping cancelled."); return 130; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { Console.Error.WriteLine(ex.Message); return 2; }
    }
}

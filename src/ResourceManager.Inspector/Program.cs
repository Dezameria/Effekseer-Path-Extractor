using System.Text.Json;
using System.Text.Json.Serialization;
using ResourceManager.Core.Models;
using ResourceManager.Core.Scanning;
using ResourceManager.Core.Validation;
using ResourceManager.Infrastructure.Effekseer;
using ResourceManager.Infrastructure.FileSystem;
using ResourceManager.Infrastructure.Search;

if (args.Length > 0 && args[0] == "map") return await MappingCommand.RunAsync(args[1..]);
if (args.Length > 0 && args[0] == "repair") return await ResourceRepairCommand.RunAsync(args[1..]);

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("Read-only Effekseer Inspector\nUsage: Inspector <effect, material or folder> [--boundary <folder>] [--search <folder>] [--output <new.json>] [--dump-editor <folder>]\nExit codes: 0 inspected (not portability certification), 1 parse/discovery error, 2 invalid arguments/output, 130 cancelled.");
    return 0;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] is not ("--boundary" or "--search" or "--output" or "--dump-editor") || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Unknown, duplicate or incomplete option: " + args[i]);
    }
    var input = Path.GetFullPath(args[0]);
    var isFolder = Directory.Exists(input);
    if (!isFolder && !File.Exists(input)) throw new ArgumentException("Input does not exist: " + input);
    var boundary = Path.GetFullPath(options.GetValueOrDefault("--boundary") ?? (isFolder ? input : Path.GetDirectoryName(input)!));
    if (!Directory.Exists(boundary)) throw new ArgumentException("Boundary directory does not exist.");
    var parser = new EffekseerResourceParser();
    if (!isFolder && !parser.CanRead(input)) throw new ArgumentException("Choose .efkefc, .efkmat or a project folder.");
    var discovery = isFolder ? DocumentDiscovery.Enumerate(input, parser.CanRead, cancellation.Token) : new DiscoveryResult([input], []);
    var scanner = new ResourceScanner(parser, new WindowsPathResolver());
    var report = await scanner.ScanAsync(discovery.Files, boundary, cancellationToken: cancellation.Token);
    if (discovery.Diagnostics.Count > 0)
    {
        var diagnostics = report.Diagnostics.Concat(discovery.Diagnostics).ToArray();
        report = report with { Diagnostics = diagnostics, Portability = PortabilityValidator.Validate(report.Documents, report.Resources, diagnostics) };
    }
    Console.WriteLine($"Read-only inspection | Boundary: {boundary}");
    Console.WriteLine($"Documents: {report.Documents.Count} | Effects: {report.Documents.Count(d => d.Format == "EFKE")} | Materials: {report.Documents.Count(d => d.Format == "EFKM")}");
    foreach (var document in report.Documents)
        Console.WriteLine($"  {Path.GetFileName(document.Path)} | {document.Format} {document.Version} | INFO {document.DependencyVersion} | Editor {document.EditorVersion} | {document.Coverage}");
    Console.WriteLine($"Resources: {report.Resources.Count} | " + string.Join(" | ", report.Resources.GroupBy(r => r.Status).Select(g => $"{g.Key}: {g.Count()}")));
    foreach (var resource in report.Resources.Where(r => r.Status != "VALID"))
    {
        var roles = string.Join(", ", resource.References.Select(r => r.Reference.Role).Distinct());
        Console.WriteLine($"  {resource.Status,-10} {resource.ResolvedPath ?? resource.Name} [{roles}]");
    }
    Console.WriteLine($"Portability: {report.Portability.Outcome.ToString().ToUpperInvariant()} (phase 0 does not certify complete editor/runtime semantics)");
    foreach (var diagnostic in report.Diagnostics.Where(d => d.IsError)) Console.WriteLine($"  {diagnostic.Code}: {diagnostic.Message}");
    SearchResult? search = null;
    if (options.TryGetValue("--search", out var searchRoot))
    {
        if (!Directory.Exists(searchRoot)) throw new ArgumentException("Search root does not exist.");
        search = await new MissingResourceFinder().FindAsync(report.Resources, searchRoot, cancellation.Token);
        foreach (var match in search.Matches)
        {
            Console.WriteLine($"  Search {match.ResourceId}: {match.Candidates.Count} candidate(s){(match.IsAmbiguous ? " / AMBIGUOUS" : "")} (no auto-selection)");
            foreach (var candidate in match.Candidates) Console.WriteLine("    " + candidate.Path);
        }
    }
    if (options.TryGetValue("--dump-editor", out var editorDirectory))
    {
        editorDirectory = Path.GetFullPath(editorDirectory);
        GuardOutput(editorDirectory, input, boundary, isFolder);
        Directory.CreateDirectory(editorDirectory);
        foreach (var document in report.Documents.Where(d => d.EditorXml is not null))
        {
            var relative = Path.GetRelativePath(isFolder ? input : Path.GetDirectoryName(input)!, document.Path);
            if (relative.StartsWith("..")) relative = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(document.Path))) + ".efkefc";
            var target = Path.Combine(editorDirectory, relative + ".xml");
            GuardOutput(target, input, boundary, isFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(document.EditorXml.AsMemory(), cancellation.Token);
        }
    }
    if (options.TryGetValue("--output", out var output))
    {
        output = Path.GetFullPath(output);
        GuardOutput(output, input, boundary, isFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        var compactReport = report with { Documents = report.Documents.Select(d => d with { EditorXml = null }).ToArray() };
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(stream, new { Report = compactReport, Search = search }, jsonOptions, cancellation.Token);
        Console.WriteLine("Report saved: " + output);
    }
    return report.Diagnostics.Any(d => d.IsError) || (search?.Diagnostics.Any(d => d.IsError) ?? false) ? 1 : 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled. Source files were not modified."); return 130; }
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
{ Console.Error.WriteLine(ex.Message); return 2; }

static void GuardOutput(string path, string input, string boundary, bool isFolder)
{
    var sourceRoot = isFolder ? input : Path.GetDirectoryName(input)!;
    if (WindowsPathResolver.IsInside(path, boundary) || WindowsPathResolver.IsInside(path, sourceRoot))
        throw new ArgumentException("Inspector output must be outside the source folder and boundary.");
    if (WindowsPathResolver.HasReparsePoint(path)) throw new ArgumentException("Output path contains a symlink/junction.");
}

using Effekseer;
using Effekseer.Command;
using Effekseer.Data;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace EffekseerAI.Host;

// One isolated Core per process. All access stays on the stdio reader thread.
internal sealed class EditorSession
{
    private readonly Dictionary<NodeBase, string> ids = new();
    private string sessionId = "";
    private long revision;
    private bool ready;
    private bool faulted;
    private string? sourcePath;
    private string? sourceHash;
    private static string Version => (string)typeof(Core).GetField("Version")!.GetRawConstantValue()!;

    public object Execute(JObject request)
    {
        var profile = typeof(EditorSession).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "EffekseerProfile").Value;
        if ((Version != "1.80.7" && Version != "1.70e") || Version != profile)
            throw new BridgeException("UnsupportedVersion", "This Core version has no tested host profile.");
        var method = RequiredString(request, "method");
        if (method == "capabilities") return new {
            protocol = 1, version = Version, host = "isolated-file-host", liveEditor = false, preview = false, ai = false,
            operations = Version == "1.80.7"
                ? new[] { "node.create", "node.rename", "node.setRendered", "color.setFixed", "scale.setFixed", "recipe.tornado" }
                : new[] { "node.create", "node.rename", "node.setRendered", "color.setFixed", "scale.setFixed" },
            save = "new-file-only", inputPolicy = "exact ToolVersion only", undo = true,
            limitations = new[] { "Color: Sprite/Model, Fixed, built-in materials, RGBA only",
                "Scale: Fixed without dynamic equation; children retain native inheritance",
                "No rendered appearance certification; save uses the selected version's official exporter" }
        };
        if (method == "new" || method == "open")
        {
            if (ready && !faulted) Guard(request);
            if (method == "open")
            {
                var path = Path.GetFullPath(RequiredString(request, "path"));
                var hash = Hash(path);
                var xml = ReadEditorDocument(path);
                var toolVersion = xml?.SelectSingleNode("/EffekseerProject/ToolVersion")?.InnerText;
                if (toolVersion != Version) throw new BridgeException("VersionMismatch", $"File is {toolVersion ?? "unknown"}; this host is {Version}. Automatic migration is disabled.");
                faulted = true;
                Core.New();
                if (!Core.LoadFrom(path) || Hash(path) != hash)
                    throw new BridgeException("LoadFailed", "File could not be loaded consistently.");
                sourcePath = path; sourceHash = hash;
            }
            else
            {
                var duration = request["durationFrames"] == null ? 120 : (int)request["durationFrames"]!;
                if (duration < 1 || duration > 3600) throw new BridgeException("InvalidDuration", "Duration must be 1–3600 frames.");
                faulted = true; Core.New(); Core.EndFrame = duration;
                sourcePath = null; sourceHash = null;
            }
            ResetSession();
            return Snapshot();
        }
        if (!ready || faulted) throw new BridgeException("SessionUnavailable", "Open or create a project first.");
        if (method == "snapshot") return Snapshot();
        Guard(request);
        switch (method)
        {
            case "select": Core.SelectedNode = Find(RequiredString(request, "nodeId")); revision++; return Snapshot();
            case "apply": return Apply(request);
            case "undo": if (CommandManager.Undo()) revision++; return Snapshot();
            case "redo": if (CommandManager.Redo()) revision++; return Snapshot();
            case "saveAs": return SaveAs(RequiredString(request, "path"));
            default: throw new BridgeException("UnknownMethod", method);
        }
    }

    private void ResetSession()
    {
        ids.Clear(); sessionId = Guid.NewGuid().ToString("N"); revision = 0; ready = true; faulted = false;
    }

    private void Guard(JObject request)
    {
        if ((string?)request["sessionId"] != sessionId || request["revision"]?.Type != JTokenType.Integer || (long?)request["revision"] != revision)
            throw new BridgeException("StaleRevision", "Read a new snapshot before editing this session.");
    }

    private string Id(NodeBase node)
    {
        if (!ids.TryGetValue(node, out var id)) ids[node] = id = Guid.NewGuid().ToString("N");
        return id;
    }

    private static IEnumerable<NodeBase> Walk(NodeBase node)
    {
        yield return node;
        for (var i = 0; i < node.Children.Count; i++)
            foreach (var descendant in Walk(node.Children[i])) yield return descendant;
    }

    private NodeBase Find(string id) => Walk(Core.Root).FirstOrDefault(n => Id(n) == id)
        ?? throw new BridgeException("NodeNotFound", "Node ID is not part of the current project.");

    private object Snapshot() => new {
        sessionId, revision, version = Version, sourcePath, sourceHash,
        selectedNodeId = SelectedId(),
        nodes = Walk(Core.Root).Select(n => new {
            id = Id(n), path = NodePath(n), parentId = n.Parent == null ? null : Id(n.Parent), name = n.Name.Value, rendered = n.IsRendered.Value,
            renderer = (n as Node)?.DrawingValues.Type.Value.ToString(),
            colorMode = (n as Node)?.DrawingValues.ColorAll.Type.Value.ToString(),
            color = n is Node cn ? new[] { cn.DrawingValues.ColorAll.Fixed.R.Value, cn.DrawingValues.ColorAll.Fixed.G.Value,
                cn.DrawingValues.ColorAll.Fixed.B.Value, cn.DrawingValues.ColorAll.Fixed.A.Value } : null,
            scaleMode = (n as Node)?.ScalingValues.Type.Value.ToString(),
            scale = n is Node sn ? new[] { sn.ScalingValues.Fixed.Scale.X.Value, sn.ScalingValues.Fixed.Scale.Y.Value, sn.ScalingValues.Fixed.Scale.Z.Value } : null,
            scaleDynamic = (n as Node)?.ScalingValues.Fixed.Scale.IsDynamicEquationEnabled.Value,
            material = (n as Node)?.RendererCommonValues.Material.Value.ToString()
        }).ToArray()
    };

    private object Apply(JObject request)
    {
        var operations = request["operations"] as JArray;
        if (operations == null || operations.Count == 0 || operations.Count > 100)
            throw new BridgeException("InvalidPlan", "A plan needs 1–100 operations.");
        // Validate the whole plan before setters. Never execute generated code or reflection setters.
        var actions = operations.Select(o => Prepare(o as JObject ?? throw new BridgeException("InvalidPlan", "Expected operation object."))).ToArray();
        CommandManager.StartCollection();
        try { foreach (var action in actions) action(); }
        catch
        {
            CommandManager.EndCollection();
            faulted = true; // Unexpected setter failure: discard the isolated session, never save it.
            throw;
        }
        CommandManager.EndCollection();
        revision++;
        return Snapshot();
    }

    private Action Prepare(JObject operation)
    {
        var kind = RequiredString(operation, "kind");
        var node = Find(RequiredString(operation, "nodeId"));
        switch (kind)
        {
#if !NETFRAMEWORK
            case "recipe.tornado":
                if (node != Core.Root) throw new BridgeException("InvalidPlan", "Tornado recipe must target the project root.");
                var textures = Path.GetFullPath(RequiredString(operation, "textureDirectory"));
                foreach (var texture in new[] { "cloud.png", "wind.png", "spark.png" })
                    if (!File.Exists(Path.Combine(textures, texture)))
                        throw new BridgeException("MissingTexture", "Missing recipe texture: " + texture);
                return () => TornadoRecipe.Create(node, textures);
#endif
            case "node.create":
                var childName = Name(operation);
                return () => node.AddChild().Name.SetValue(childName);
            case "node.rename":
                var name = Name(operation);
                return () => node.Name.SetValue(name);
            case "node.setRendered":
                if (operation["value"]?.Type != JTokenType.Boolean) throw new BridgeException("InvalidRequest", "Rendered value must be a boolean.");
                var rendered = (bool)operation["value"]!;
                return () => node.IsRendered.SetValue(rendered);
            case "color.setFixed":
                if (node is not Node colorNode ||
                    (colorNode.DrawingValues.Type.Value != RendererValues.ParamaterType.Sprite && colorNode.DrawingValues.Type.Value != RendererValues.ParamaterType.Model) ||
                    colorNode.DrawingValues.ColorAll.Type.Value != StandardColorType.Fixed ||
                    colorNode.RendererCommonValues.Material.Value == RendererCommonValues.MaterialType.File)
                    throw new BridgeException("UnsupportedBinding", "Fixed color requires Sprite/Model with a built-in material and Fixed color mode.");
                var rgba = operation["rgba"] as JArray;
                if (rgba == null || rgba.Count != 4 || rgba.Any(c => c.Type != JTokenType.Integer || (long)c < 0 || (long)c > 255))
                    throw new BridgeException("InvalidColor", "RGBA requires four integers in 0–255.");
                var values = rgba.Select(c => (int)c).ToArray();
                if (colorNode.DrawingValues.ColorAll.Fixed.GetColorSpace().ToString() != "RGBA")
                    throw new BridgeException("UnsupportedBinding", "Only RGBA color space is currently supported.");
                return () => colorNode.DrawingValues.ColorAll.Fixed.SetValue(values[0], values[1], values[2], values[3]);
            case "scale.setFixed":
                if (node is not Node scaleNode || scaleNode.ScalingValues.Type.Value != ScaleValues.ParamaterType.Fixed || scaleNode.ScalingValues.Fixed.Scale.IsDynamicEquationEnabled.Value)
                    throw new BridgeException("UnsupportedBinding", "Scale requires Fixed mode without a dynamic equation.");
                var xyz = operation["xyz"] as JArray;
                if (xyz == null || xyz.Count != 3 || xyz.Any(c => c.Type != JTokenType.Float && c.Type != JTokenType.Integer))
                    throw new BridgeException("InvalidScale", "XYZ requires three finite numbers.");
                var scale = xyz.Select(c => (float)c).ToArray();
                if (scale.Any(v => float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 100000))
                    throw new BridgeException("InvalidScale", "Scale must be finite and between -100000 and 100000.");
                return () => { var s = scaleNode.ScalingValues.Fixed.Scale; s.X.SetValue(scale[0]); s.Y.SetValue(scale[1]); s.Z.SetValue(scale[2]); };
            default: throw new BridgeException("UnsupportedOperation", kind);
        }
    }

    private object SaveAs(string requestedPath)
    {
        var target = Path.GetFullPath(requestedPath);
        if (!string.Equals(Path.GetExtension(target), ".efkefc", StringComparison.OrdinalIgnoreCase))
            throw new BridgeException("InvalidPath", "Output must be an .efkefc file.");
        if (File.Exists(target)) throw new BridgeException("OutputExists", "Choose a new filename; existing files are never overwritten by this host.");
        if (sourcePath != null && Hash(sourcePath) != sourceHash)
            throw new BridgeException("SourceChanged", "The source changed on disk. Reopen it before saving.");
        var parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) throw new BridgeException("InvalidPath", "Output directory must exist.");
        var stage = Path.Combine(parent, ".effekseer-ai-" + Guid.NewGuid().ToString("N") + ".efkefc");
        try
        {
            faulted = true;
            Core.SaveTo(stage);
            var expected = Core.SaveAsXmlDocument(Core.Root).OuterXml;
            Core.New();
            if (!Core.LoadFrom(stage)) throw new BridgeException("RoundTripFailed", "Saved file could not be reloaded.");
            var actual = Core.SaveAsXmlDocument(Core.Root).OuterXml;
            if (!EditorXmlComparer.Equivalent(expected, actual))
            {
                var difference = 0;
                while (difference < Math.Min(expected.Length, actual.Length) && expected[difference] == actual[difference]) difference++;
                throw new BridgeException("RoundTripFailed", "Save/reload changed the project at " + difference + ": expected "
                    + expected.Substring(difference, Math.Min(160, expected.Length - difference)) + "; actual "
                    + actual.Substring(difference, Math.Min(160, actual.Length - difference)));
            }
            if (sourcePath != null && Hash(sourcePath) != sourceHash)
                throw new BridgeException("SourceChanged", "The source changed while exporting. Output was not published.");
            File.Move(stage, target);
            Core.Root.SetFullPath(target);
            sourcePath = target; sourceHash = Hash(target);
            ResetSession(); // Reload replaces identities and clears Undo history.
            return new { path = target, sha256 = sourceHash, verification = "official-core-save-reload-structure-with-random-float-tolerance-1e-6", undoHistoryReset = true, snapshot = Snapshot() };
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }

    private static string Name(JObject operation)
    {
        var value = RequiredString(operation, "name");
        if (value.Length > 256) throw new BridgeException("InvalidName", "Node name exceeds 256 characters.");
        try { System.Xml.XmlConvert.VerifyXmlChars(value); }
        catch (System.Xml.XmlException) { throw new BridgeException("InvalidName", "Node name contains characters that cannot be saved in XML."); }
        return value;
    }
    private string? SelectedId()
    {
        if (Core.SelectedNode != null && !Walk(Core.Root).Contains(Core.SelectedNode)) Core.SelectedNode = null;
        return Core.SelectedNode == null ? null : Id(Core.SelectedNode);
    }
    private static string NodePath(NodeBase node)
    {
        if (node.Parent == null) return "root";
        for (var i = 0; i < node.Parent.Children.Count; i++)
            if (ReferenceEquals(node.Parent.Children[i], node))
                return node.Parent == Core.Root ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : NodePath(node.Parent) + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        throw new InvalidOperationException("Detached node.");
    }
    private static System.Xml.XmlDocument? ReadEditorDocument(string path)
    {
        // The official file reader is internal in both pinned releases. This fixed read-only
        // binding is never derived from a request; all mutation uses public typed setters.
        var type = typeof(Core).Assembly.GetType("Effekseer.IO.EfkEfc", true)!;
        var reader = Activator.CreateInstance(type, true);
        var result = type.GetMethod("Load", new[] { typeof(string) })!.Invoke(reader, new object[] { path });
        if (Version == "1.70e") return (System.Xml.XmlDocument?)result;
        return result is true ? (System.Xml.XmlDocument?)type.GetField("EditorData")!.GetValue(reader) : null;
    }
    internal static string RequiredString(JObject value, string key) => value[key]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)value[key])
        ? (string)value[key]! : throw new BridgeException("InvalidRequest", $"Missing string: {key}");
    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
}

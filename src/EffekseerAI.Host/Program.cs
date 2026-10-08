using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EffekseerAI.Host;

internal static class Program
{
    private static int Main()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var session = new EditorSession();
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            JToken? id = null;
            try
            {
                if (line.Length > 1_048_576) throw new BridgeException("RequestTooLarge", "Maximum request size is 1 MiB.");
                var request = JObject.Parse(line, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                id = request["id"];
                object result;
                var protocolOutput = Console.Out;
                // Older official loaders write diagnostics to Console.Out.
                // Keep the JSON protocol separate from upstream diagnostics.
                try { Console.SetOut(Console.Error); result = session.Execute(request); }
                finally { Console.SetOut(protocolOutput); }
                Console.WriteLine(JsonConvert.SerializeObject(new { id, ok = true, result }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonConvert.SerializeObject(new { id, ok = false,
                    error = new { code = (ex as BridgeException)?.Code ?? "HostFailure", message = ex.Message } }));
            }
        }
        return 0;
    }
}

internal sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

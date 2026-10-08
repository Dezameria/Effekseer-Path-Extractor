using ResourceManager.Core.Models;

namespace ResourceManager.Core.Validation;

public static class PortabilityValidator
{
    public static PortabilityResult Validate(IEnumerable<ParsedDocument> documents,
        IEnumerable<ResourceEntry> resources, IEnumerable<Diagnostic> diagnostics)
    {
        var reasons = new List<string>();
        var failed = false;
        var incomplete = false;
        foreach (var resource in resources)
        {
            // Material defaults and inactive editor fields are inspected, but their runtime
            // necessity cannot be inferred without material parameter/override analysis.
            var required = resource.References.Where(r => r.Reference.Role is
                ReferenceRole.Metadata or ReferenceRole.Runtime or ReferenceRole.CompatibilityRuntime).ToArray();
            if (required.Any(r => r.Audit.Status is "MISSING" or "EXTERNAL" || r.Audit.IsAbsolute))
            {
                failed = true;
                reasons.Add($"{resource.Name}: required reference is missing, external, or bound to an absolute location.");
            }
            if (resource.References.Any(r => r.Audit.Status is "ERROR" or "UNRESOLVED"))
                incomplete = true;
        }
        foreach (var document in documents)
        {
            if (document.Coverage != ScanCoverage.Complete)
            {
                incomplete = true;
                reasons.Add($"{System.IO.Path.GetFileName(document.Path)}: {document.Coverage}; full portability is not certified.");
            }
        }
        if (diagnostics.Any(d => d.IsError)) incomplete = true;
        var allDocuments = documents.ToArray();
        if (allDocuments.Length == 0)
        {
            incomplete = true;
            reasons.Add("No supported entry documents were scanned.");
        }
        if (incomplete && reasons.Count == 0) reasons.Add("Some references or filesystem entries could not be verified.");
        if (!failed && !incomplete) reasons.Add("All known references are relative and confined to the export boundary.");
        return new(failed ? PortabilityOutcome.Fail : incomplete ? PortabilityOutcome.Inconclusive : PortabilityOutcome.Pass,
            reasons.Distinct().ToArray());
    }
}

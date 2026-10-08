using System.Text.RegularExpressions;
using ResourceManager.Core.Models;

namespace ResourceManager.Infrastructure.Effekseer;

public sealed record EffectFormatProfile(string Name, string EditorPattern, int LayoutVersion);
public sealed record EffectCompatibility(EffectFormatProfile? Profile, IReadOnlyList<string> Reasons)
{
    public bool CanMap => Profile is not null && Reasons.Count == 0;
    public string Describe(ParsedDocument document) =>
        $"Effekseer {document.EditorVersion ?? "ไม่ทราบเวอร์ชัน"} · {document.Format} {document.Version?.ToString() ?? "?"} · INFO {document.DependencyVersion?.ToString() ?? "?"} · BIN_ {string.Join(", ", document.Chunks.Where(c => c.Id == "BIN_").Select(c => c.Version?.ToString() ?? "?"))}";
    public string Rejection(ParsedDocument document) => $"ยัง Map ไฟล์นี้ไม่ได้ ({Describe(document)}): " + string.Join("; ", Reasons);
}

/// <summary>Stable editor families with resource tables verified against upstream exporters.
/// A matching release name alone never grants write access.</summary>
public static class EffectFormatCompatibility
{
    public static IReadOnlyList<EffectFormatProfile> Profiles { get; } = Array.AsReadOnly<EffectFormatProfile>([
        new("1.60–1.62", @"^1\.(?:60|61|62)(?:[a-z]|\.[0-9]+)?$", 1610),
        new("1.70", @"^1\.70(?:[a-z]|\.[0-9]+)?$", 1710),
        new("1.80", @"^1\.80(?:[a-z]|\.[0-9]+)?$", 1810)
    ]);

    public static bool IsKnownInfo(int version) => version is 1610 or 1710 or 1810;
    public static bool IsTypedInfo(int version) => version is 1710 or 1810;
    public static bool IsKnownRuntime(int? version) => version is 1500 or 1610 or 1710 or 1810;

    public static EffectCompatibility Evaluate(ParsedDocument document)
    {
        var reasons = new List<string>();
        var profile = Profiles.FirstOrDefault(p => Regex.IsMatch(document.EditorVersion ?? "", p.EditorPattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
        if (document.Format != "EFKE" || document.Version != 0) reasons.Add("รองรับการ Map เฉพาะ .efkefc แบบ EFKE container 0");
        if (profile is null) reasons.Add("ยังไม่มีรูปแบบที่รองรับเวอร์ชันนี้ (รองรับรุ่น stable 1.60–1.62, 1.70 และ 1.80; alpha/beta/รุ่นอื่นยังตรวจสอบได้อย่างเดียว)");
        if (document.EditorXml is null) reasons.Add("อ่านข้อมูล EDIT ไม่สำเร็จ");
        foreach (var error in document.Diagnostics.Where(d => d.IsError)) reasons.Add(error.Message);
        foreach (var id in new[] { "INFO", "EDIT" })
            if (document.Chunks.Count(c => c.Id == id) != 1) reasons.Add($"ต้องมี {id} เพียงหนึ่งส่วน");
        var unknownChunks = document.Chunks.Where(c => c.Id is not ("INFO" or "EDIT" or "BIN_")).Select(c => c.Id).Distinct().ToArray();
        if (unknownChunks.Length > 0) reasons.Add("พบส่วนข้อมูลที่ยังเขียนไม่ได้: " + string.Join(", ", unknownChunks));
        var runtimes = document.Chunks.Where(c => c.Id == "BIN_").ToArray();
        if (runtimes.Length == 0) reasons.Add("ไม่พบข้อมูล BIN_");
        foreach (var runtime in runtimes.Where(c => !IsKnownRuntime(c.Version))) reasons.Add($"ยังไม่รองรับตาราง resource ของ BIN_ {runtime.Version}");
        if (profile is not null)
        {
            if (document.DependencyVersion != profile.LayoutVersion)
                reasons.Add($"กลุ่ม {profile.Name} ต้องใช้ INFO {profile.LayoutVersion} แต่ไฟล์นี้เป็น {document.DependencyVersion}");
            if (runtimes.Length > 0 && (runtimes.Any(c => c.Version is null) || runtimes.Max(c => c.Version) != profile.LayoutVersion))
                reasons.Add($"กลุ่ม {profile.Name} ต้องมี BIN_ หลัก {profile.LayoutVersion} และส่วน compatibility ที่เก่ากว่า");
        }
        return new(profile, reasons.Distinct().ToArray());
    }
}

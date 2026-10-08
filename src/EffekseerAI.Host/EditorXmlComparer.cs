using System.Globalization;
using System.Xml;

namespace EffekseerAI.Host;

internal static class EditorXmlComparer
{
    public static bool Equivalent(string expected, string actual)
    {
        if (expected == actual) return true;
        var left = new XmlDocument { XmlResolver = null };
        var right = new XmlDocument { XmlResolver = null };
        left.LoadXml(expected); right.LoadXml(actual);
        var a = left.SelectNodes("//*[not(*)]")!;
        var b = right.SelectNodes("//*[not(*)]")!;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i]!; var y = b[i]!;
            if (x.Name != y.Name) return false;
            if (x.InnerText == y.InnerText) continue;
            // FloatWithRandom recomputes Center from Min/Max on import. Do not relax
            // identifiers, resource paths, enums, integers, node ordering or attributes.
            if (x.Name != "Center" && x.Name != "Min" && x.Name != "Max") continue;
            if (!x.InnerText.Contains(".") && !y.InnerText.Contains(".")) continue;
            if (double.TryParse(x.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)
                && double.TryParse(y.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var q)
                && !double.IsNaN(p) && !double.IsNaN(q) && !double.IsInfinity(p) && !double.IsInfinity(q)
                && Math.Abs(p - q) <= 0.000001) y.InnerText = x.InnerText;
        }
        return left.OuterXml == right.OuterXml;
    }
}

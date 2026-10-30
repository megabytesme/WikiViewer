using System;
using System.Linq;
using System.Xml.Linq;

namespace WikiViewer.Core.Services
{
    public static class NativeSvgService
    {
        /// <summary>Retain static vector markup; native SVG rendering does not run page code.</summary>
        public static string Prepare(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Length > 1000000 || source.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("<!ENTITY", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            try
            {
                var root = XElement.Parse(source, LoadOptions.PreserveWhitespace);
                if (!root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase)) return null;
                string[] active = { "script", "foreignObject", "animate", "animateMotion", "animateTransform", "set" };
                foreach (var element in root.Descendants().Where(e => active.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)).ToList()) element.Remove();
                XNamespace svg = "http://www.w3.org/2000/svg";
                foreach (var element in root.DescendantsAndSelf())
                {
                    if (element.Name.NamespaceName.Length == 0) element.Name = svg + element.Name.LocalName;
                    foreach (var attribute in element.Attributes().Where(a => a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                        (a.Name.LocalName == "href" && !a.Value.StartsWith("#", StringComparison.Ordinal))).ToList()) attribute.Remove();
                }
                return root.ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception) { return null; }
        }
    }
}

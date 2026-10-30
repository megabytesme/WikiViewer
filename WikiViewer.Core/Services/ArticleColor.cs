using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WikiViewer.Core.Services
{
    public static class ArticleColor
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { { "black", "000000" }, { "white", "ffffff" }, { "red", "ff0000" }, { "blue", "0000ff" }, { "green", "008000" },
          { "gray", "808080" }, { "grey", "808080" }, { "silver", "c0c0c0" }, { "yellow", "ffff00" }, { "orange", "ffa500" },
          { "purple", "800080" }, { "teal", "008080" }, { "navy", "000080" }, { "maroon", "800000" }, { "aqua", "00ffff" },
          { "fuchsia", "ff00ff" }, { "lime", "00ff00" }, { "transparent", "00000000" } };

        public static uint? Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string input = value.Trim(), named;
            if (Names.TryGetValue(input, out named)) input = "#" + named;
            if (input.StartsWith("#"))
            {
                string hex = input.Substring(1);
                if (hex.Length == 3 || hex.Length == 4)
                { string expanded = ""; foreach (char c in hex) expanded += new string(c, 2); hex = expanded; }
                uint number;
                if ((hex.Length != 6 && hex.Length != 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out number)) return null;
                return hex.Length == 6 ? 0xff000000 | number : (number >> 8) | ((number & 255) << 24);
            }
            var rgb = Regex.Match(input, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)$", RegexOptions.IgnoreCase);
            if (!rgb.Success) return null;
            uint red, green, blue;
            double alpha = 1;
            if (!uint.TryParse(rgb.Groups[1].Value, out red) || !uint.TryParse(rgb.Groups[2].Value, out green) || !uint.TryParse(rgb.Groups[3].Value, out blue) || red > 255 || green > 255 || blue > 255) return null;
            if (rgb.Groups[4].Success && (!double.TryParse(rgb.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) || alpha < 0 || alpha > 1)) return null;
            return ((uint)Math.Round(alpha * 255) << 24) | (red << 16) | (green << 8) | blue;
        }
    }
}

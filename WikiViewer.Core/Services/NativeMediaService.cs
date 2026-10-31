using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public static class NativeMediaService
    {
        public static bool IsErrorDocument(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return true;
            string prefix = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart('\uFEFF', ' ', '\t', '\r', '\n').ToLowerInvariant();
            return prefix.StartsWith("<!doctype html") || prefix.StartsWith("<html") || prefix.StartsWith("<head") || prefix.StartsWith("<body");
        }
        public static List<Uri> ImageSources(HtmlNode image, Uri page)
        {
            var result = new List<Uri>();
            Action<string> add = value =>
            {
                Uri uri;
                if (!string.IsNullOrWhiteSpace(value) && Uri.TryCreate(page, WebUtility.HtmlDecode(value), out uri) &&
                    (uri.Scheme == "https" || uri.Scheme == "http") && !result.Contains(uri)) result.Add(uri);
            };
            add(image.GetAttributeValue("data-src", null));
            add(image.GetAttributeValue("data-original", null));
            add(image.GetAttributeValue("src", null));
            var sets = new[] { image.GetAttributeValue("data-srcset", ""), image.GetAttributeValue("srcset", "") };
            var picture = image.ParentNode;
            if (picture?.Name == "picture") sets = sets.Concat(picture.ChildNodes.Where(n => n.Name == "source").Select(n => n.GetAttributeValue("srcset", ""))).ToArray();
            foreach (string set in sets)
                foreach (Match match in Regex.Matches(set, @"(?:^|,\s*)(\S+?)(?:\s+[\d.]+[wx])?(?=\s*,|$)")) add(match.Groups[1].Value);
            return result.Take(6).ToList();
        }

        public static List<NativeMediaSource> PlaybackSources(IEnumerable<NativeMediaSource> sources)
            => sources.OrderBy(CodecRank).ThenBy(s => s.Height > 0 ? Math.Abs(s.Height - 480) : 0).ToList();

        private static int CodecRank(NativeMediaSource source)
        {
            string type = (source.ContentType ?? "").ToLowerInvariant(), path = source.Uri.AbsolutePath.ToLowerInvariant();
            if (type.Contains("audio/mpeg") || path.EndsWith(".mp3")) return 0;
            if (type.Contains("mp4") || type.Contains("aac") || type.Contains("mpeg") || type.Contains("quicktime") ||
                path.EndsWith(".mp4") || path.EndsWith(".m4a") || path.EndsWith(".mov")) return 1;
            if (type.Contains("webm") || path.EndsWith(".webm")) return 2;
            return 3;
        }

        public static NativeMediaSource ReadSource(HtmlNode source, Uri page)
        {
            Uri uri;
            string value = source.GetAttributeValue("data-src", null) ?? source.GetAttributeValue("src", "");
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(page, WebUtility.HtmlDecode(value), out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            int height; int.TryParse(source.GetAttributeValue("data-height", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
            string type = WebUtility.HtmlDecode(source.GetAttributeValue("type", ""));
            string label = WebUtility.HtmlDecode(source.GetAttributeValue("data-title", null) ?? source.GetAttributeValue("title", null) ?? source.GetAttributeValue("data-transcodekey", null));
            if (string.IsNullOrWhiteSpace(label)) label = (height > 0 ? height + "p · " : "") + (type.Length > 0 ? type : Uri.UnescapeDataString(uri.Segments.Last()));
            return new NativeMediaSource { Uri = uri, ContentType = type, Label = label, Height = Math.Max(0, Math.Min(10000, height)) };
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public sealed class NativeArticleSection
    {
        public int Index { get; set; }
        public string Title { get; set; } = "Introduction";
        public override string ToString() => Title;
        public List<NativeArticleNode> Nodes { get; } = new List<NativeArticleNode>();
        public HashSet<string> Anchors { get; } = new HashSet<string>();
    }

    public static class ArticleSectionService
    {
        /// <summary>Split at section headings, retaining floats and source order inside each section.</summary>
        public static List<NativeArticleSection> Split(IEnumerable<NativeArticleNode> nodes)
        {
            var sections = new List<NativeArticleSection>();
            var current = new NativeArticleSection();
            foreach (var node in nodes)
            {
                bool heading = node.Kind == NativeArticleNodeKind.Heading || (node.TagName == "div" || node.TagName == "section") && node.Children.Any(n => n.Kind == NativeArticleNodeKind.Heading);
                if (heading && current.Nodes.Count > 0) { sections.Add(current); current = new NativeArticleSection { Index = sections.Count }; }
                if (heading) current.Title = Text(node).Trim();
                current.Nodes.Add(node);
                Collect(node, current.Anchors);
            }
            if (current.Nodes.Count > 0) sections.Add(current);
            return sections;
        }
        private static void Collect(NativeArticleNode node, HashSet<string> anchors)
        {
            if (!string.IsNullOrEmpty(node.Id)) anchors.Add(node.Id);
            foreach (var child in node.Children) Collect(child, anchors);
        }
        private static string Text(NativeArticleNode node) => node.Kind == NativeArticleNodeKind.Text ? node.Text : string.Concat(node.Children.Select(Text));
    }
}

using System;
using System.Text.RegularExpressions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WikiViewer.Core.Services
{
    public static class CacheKeyService
    {
        public static string Title(string title) => Regex.Replace((title ?? "").Replace('_', ' '), " +", " ").Trim();
        public static string Article(Guid wiki, string title) => wiki.ToString() + "_" + Title(title);
        public static bool EquivalentTitle(string left, string right)
        {
            left = Title(left); right = Title(right);
            return left == right || left.Length == right.Length && left.Length > 0 && char.ToUpperInvariant(left[0]) == char.ToUpperInvariant(right[0]) && left.Substring(1) == right.Substring(1);
        }
        public static string Hash(string value)
        {
            using (var sha = SHA1.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
        }
    }
}

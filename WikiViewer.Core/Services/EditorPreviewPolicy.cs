using System.Text;

namespace WikiViewer.Core.Services
{
    public static class EditorPreviewPolicy
    {
        // NavigateToString is limited to 2 MB. Native consumes the document directly.
        public const int WebByteLimit = 2 * 1024 * 1024;
        public static bool CanDisplayWeb(string html) => Encoding.UTF8.GetByteCount(html ?? "") <= WebByteLimit;
    }
}

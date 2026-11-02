using WikiViewer.Core;
using WikiViewer.Core.Services;
using Xunit;

namespace WikiViewer.Tests;

public class CorePortabilityTests
{
    [Fact]
    public void CoreHasNoWindowsOrUiFrameworkAssemblyReferences()
    {
        var assembly = typeof(NativeArticleDocumentService).Assembly;
        Assert.Equal("WikiViewer.Core", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name.StartsWith("Windows") || reference.Name.StartsWith("Microsoft.UI") ||
            reference.Name.Contains("PresentationFramework") || reference.Name.Contains("WikiViewer.Uwp"));
        Assert.Equal(typeof(EditorThemeMode), typeof(AppSettings).GetProperty(nameof(AppSettings.EditorTheme)).PropertyType);
    }
}

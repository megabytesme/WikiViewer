using Newtonsoft.Json;

namespace WikiViewer.Core.Services
{
    public static class NativeSectionSignature
    {
        public static string Get(NativeArticleSection section) => CacheKeyService.Hash(JsonConvert.SerializeObject(section.Nodes));
    }
}

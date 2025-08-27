using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;

namespace WikiViewer.Core.Services
{
    public class ConnectionTestResult
    {
        public bool IsSuccess { get; set; }
        public ConnectionMethod Method { get; set; }
        public WikiPaths Paths { get; set; }
    }

    public static class ConnectionTesterService
    {
        public static async Task<ConnectionTestResult> FindWorkingMethodAndPathsAsync(
            string baseUrl,
            IApiWorkerFactory factory
        )
        {
            var methodsToTest = new[]
            {
                ConnectionMethod.HttpClient,
                ConnectionMethod.WebView,
                ConnectionMethod.HttpClientProxy,
            };

            foreach (var method in methodsToTest)
            {
                Debug.WriteLine($"[ConnectionTester] Starting test for: {method}");
                using (
                    var worker = factory.CreateApiWorker(
                        new Models.WikiInstance { PreferredConnectionMethod = method }
                    )
                )
                {
                    try
                    {
                        await worker.InitializeAsync(baseUrl);
                        var paths = await WikiPathDetectorService.DetectPathsAsync(baseUrl, worker);
                        if (!paths.WasDetectedSuccessfully)
                        {
                            Debug.WriteLine(
                                $"[ConnectionTester] FAILED (Path Detection): {method}"
                            );
                            continue;
                        }

                        var tempWikiForApiTest = new Models.WikiInstance
                        {
                            BaseUrl = baseUrl,
                            ScriptPath = paths.ScriptPath,
                        };
                        string testApiUrl =
                            $"{tempWikiForApiTest.ApiEndpoint}?action=query&meta=siteinfo&format=json";

                        Debug.WriteLine(
                            $"[ConnectionTester] Performing API validation call for {method} to {testApiUrl}"
                        );
                        string jsonResult = await worker.GetJsonFromApiAsync(testApiUrl);

                        if (string.IsNullOrEmpty(jsonResult))
                        {
                            Debug.WriteLine(
                                $"[ConnectionTester] FAILED (API Call returned no JSON): {method}"
                            );
                            continue;
                        }

                        Debug.WriteLine($"[ConnectionTester] SUCCESS for: {method}");
                        return new ConnectionTestResult
                        {
                            IsSuccess = true,
                            Method = method,
                            Paths = paths,
                        };
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[ConnectionTester] FAILED (Exception) for {method}: {ex.Message}"
                        );
                    }
                }
            }

            return new ConnectionTestResult { IsSuccess = false };
        }
    }
}

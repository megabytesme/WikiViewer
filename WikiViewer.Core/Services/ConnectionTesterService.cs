using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WikiViewer.Core.Enums;
using WikiViewer.Core.Interfaces;
using WikiViewer.Core.Models;

namespace WikiViewer.Core.Services
{
    public class ConnectionTestResult
    {
        public ConnectionMethod Method { get; set; }
        public bool IsSuccess { get; set; }
        public WikiPaths DetectedPaths { get; set; }
    }

    public static class ConnectionTesterService
    {
        private static readonly List<ConnectionMethod> TestOrder = new List<ConnectionMethod>
        {
            ConnectionMethod.HttpClient,
            ConnectionMethod.WebView,
            ConnectionMethod.HttpClientProxy,
        };

        public static async Task<ConnectionTestResult> FindWorkingMethodAndPathsAsync(
            string baseUrl,
            IApiWorkerFactory factory
        )
        {
            var cts = new CancellationTokenSource();
            var tasks = new List<Task<ConnectionTestResult>>();

            foreach (var method in TestOrder)
            {
                tasks.Add(
                    Task.Run(
                        async () =>
                        {
                            if (cts.IsCancellationRequested)
                                return new ConnectionTestResult { IsSuccess = false };

                            Debug.WriteLine($"[ConnectionTester] Starting test for: {method}");
                            var tempWiki = new WikiInstance
                            {
                                BaseUrl = baseUrl,
                                PreferredConnectionMethod = method,
                            };
                            using (var worker = factory.CreateApiWorker(tempWiki))
                            {
                                try
                                {
                                    await worker.InitializeAsync(baseUrl);
                                    var paths = await WikiPathDetectorService.DetectPathsAsync(
                                        baseUrl,
                                        worker
                                    );
                                    if (paths.WasDetectedSuccessfully)
                                    {
                                        Debug.WriteLine($"[ConnectionTester] SUCCESS for: {method}");
                                        cts.Cancel();
                                        return new ConnectionTestResult
                                        {
                                            Method = method,
                                            IsSuccess = true,
                                            DetectedPaths = paths,
                                        };
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(
                                        $"[ConnectionTester] FAILED for {method}: {ex.Message}"
                                    );
                                }
                            }
                            return new ConnectionTestResult { Method = method, IsSuccess = false };
                        },
                        cts.Token
                    )
                );
            }

            var results = await Task.WhenAll(tasks);

            foreach (var method in TestOrder)
            {
                var successResult = results.FirstOrDefault(r => r.IsSuccess && r.Method == method);
                if (successResult != null)
                    return successResult;
            }

            return new ConnectionTestResult { IsSuccess = false };
        }
    }
}

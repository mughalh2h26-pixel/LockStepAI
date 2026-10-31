using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LockStepAI.Services
{
    public class InferenceService : IDisposable
    {
        private Process? _serverProcess;

        // SECURITY: Dynamic port and API Key
        private int _serverPort;
        private string _apiKey = string.Empty;

        private readonly object _outputLock = new();
        private readonly StringBuilder _serverOutput = new();

        public string StartupDiagnostic { get; private set; } = "";
        public string StartupLogPath { get; private set; } = "";

        private readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
        private readonly HttpClient _inferenceClient = new() { Timeout = TimeSpan.FromMinutes(5) };

        public async Task<bool> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string serverExe = Path.Combine(baseDir, "BitNet", "llama-server.exe");
            string modelPath = Path.Combine(baseDir, "BitNet", "ggml-model-i2_s.gguf");

            // Get a guaranteed available port from Windows
            _serverPort = GetAvailablePort();

            // Generate a random API Key
            _apiKey = Guid.NewGuid().ToString("N");

            // Configure the HttpClient to automatically send the API key
            _inferenceClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            StartupLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LockStepAI",
                "logs",
                $"startup-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
                File.WriteAllText(StartupLogPath, "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Inference] Could not initialize startup log: {ex}");
                StartupLogPath = "";
            }

            LogStartup("Inference startup diagnostics beginning.");
            LogStartup($"Base directory: {baseDir}");
            LogStartup($"Server executable: {serverExe}");
            LogStartup($"Model file: {modelPath}");
            LogStartup($"Selected dynamic port: {_serverPort}");

            if (!File.Exists(serverExe) || !File.Exists(modelPath))
            {
                string missingFiles = string.Join(Environment.NewLine, new[] { serverExe, modelPath }.Where(path => !File.Exists(path)));
                StartupDiagnostic = $"Required inference files are missing:{Environment.NewLine}{missingFiles}";
                LogStartup(StartupDiagnostic);
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = serverExe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.Combine(baseDir, "BitNet")
                };

                // Base Arguments
                startInfo.ArgumentList.Add("-m");
                startInfo.ArgumentList.Add(modelPath);
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("4096");
                startInfo.ArgumentList.Add("--port");
                startInfo.ArgumentList.Add(_serverPort.ToString());
                startInfo.ArgumentList.Add("--host");
                startInfo.ArgumentList.Add("127.0.0.1");
                startInfo.ArgumentList.Add("--api-key");
                startInfo.ArgumentList.Add(_apiKey);

                //dynamicall calculate optimal threads based ohi hi n physical cores, but limit to 1-4 threads for stability
                int optimalThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

                // NATIVE C++ PERFORMANCE OPTIMIZATIONS
                startInfo.ArgumentList.Add("-t");           // Thread count
                startInfo.ArgumentList.Add(optimalThreads.ToString());
                startInfo.ArgumentList.Add("-np");          // Number of parallel slots
                startInfo.ArgumentList.Add("1");            // 1 slot = full 4096 context for the agent
                startInfo.ArgumentList.Add("--prio");       // Native Priority
                startInfo.ArgumentList.Add("2");            // 2 = High Priority
                        // Lock model in RAM (prevents swapping)

                _serverProcess = new Process { StartInfo = startInfo };
                _serverProcess.OutputDataReceived += (s, e) => CaptureServerOutput("stdout", e.Data);
                _serverProcess.ErrorDataReceived += (s, e) => CaptureServerOutput("stderr", e.Data);

                _serverProcess.Start();
                LogStartup($"Started llama-server with PID {_serverProcess.Id}.");
                ChildProcessTracker.Track(_serverProcess);

                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                const int startupTimeoutSeconds = 300;
                bool ready = await WaitForServerAsync(startupTimeoutSeconds, cancellationToken);

                if (ready)
                {
                    LogStartup("Inference server is healthy and running with native C++ priority flags.");
                }
                else if (string.IsNullOrEmpty(StartupDiagnostic))
                {
                    StartupDiagnostic = $"The inference server did not become healthy before the {startupTimeoutSeconds}-second timeout.";
                }

                return ready;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StartupDiagnostic = "Inference server startup was cancelled.";
                LogStartup(StartupDiagnostic);
                StopServerProcess();
                throw;
            }
            catch (Exception ex)
            {
                StartupDiagnostic = $"Failed to launch or monitor llama-server:{Environment.NewLine}{ex}";
                LogStartup(StartupDiagnostic);
                return false;
            }
        }

        // Helper method to get a guaranteed free port from the OS
        private int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private void LogStartup(string message)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(entry);
            if (string.IsNullOrEmpty(StartupLogPath)) return;

            try
            {
                File.AppendAllText(StartupLogPath, entry + Environment.NewLine);
            }
            catch { }
        }

        private void CaptureServerOutput(string streamName, string? line)
        {
            if (line == null) return;

            lock (_outputLock)
            {
                _serverOutput.AppendLine($"[{streamName}] {line}");
                if (_serverOutput.Length > 16000)
                    _serverOutput.Remove(0, _serverOutput.Length - 16000);
            }

            LogStartup($"[{streamName}] {line}");
        }

        private string GetServerOutputTail()
        {
            lock (_outputLock)
            {
                string output = _serverOutput.ToString();
                return output.Length <= 6000 ? output : output[^6000..];
            }
        }

        private async Task<bool> WaitForServerAsync(int timeoutSeconds, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            string lastProbeFailure = "No health response received yet.";

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_serverProcess == null)
                {
                    StartupDiagnostic = "The server process was not created.";
                    LogStartup(StartupDiagnostic);
                    return false;
                }

                if (_serverProcess.HasExited)
                {
                    _serverProcess.WaitForExit();
                    StartupDiagnostic = $"llama-server exited during startup with exit code {_serverProcess.ExitCode}.{Environment.NewLine}{GetServerOutputTail()}";
                    LogStartup(StartupDiagnostic);
                    return false;
                }

                try
                {
                    // Health checks typically don't require the API key
                    using var response = await _healthClient.GetAsync(
                        $"http://127.0.0.1:{_serverPort}/health",
                        cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        LogStartup($"Health check succeeded with HTTP {(int)response.StatusCode} after {stopwatch.Elapsed.TotalSeconds:F1} seconds.");
                        return true;
                    }

                    string body = await response.Content.ReadAsStringAsync(cancellationToken);
                    lastProbeFailure = $"Health endpoint returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
                }
                catch (Exception ex) when (
                    !cancellationToken.IsCancellationRequested &&
                    (ex is HttpRequestException || ex is TaskCanceledException))
                {
                    lastProbeFailure = $"Health request failed: {ex.Message}";
                }

                if ((int)stopwatch.Elapsed.TotalSeconds % 10 == 0)
                    LogStartup($"Still waiting ({stopwatch.Elapsed.TotalSeconds:F0}s). {lastProbeFailure}");

                await Task.Delay(1000, cancellationToken);
            }

            StartupDiagnostic = $"Inference server startup timed out after {timeoutSeconds} seconds.{Environment.NewLine}{lastProbeFailure}{Environment.NewLine}{GetServerOutputTail()}";
            LogStartup(StartupDiagnostic);
            return false;
        }

        public async Task WarmupAsync()
        {
            try
            {
                var payload = new { messages = new[] { new { role = "user", content = "hi" } }, n_predict = 1, stream = false };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                // Use dynamic port (API key is injected via _inferenceClient DefaultRequestHeaders)
                await _inferenceClient.PostAsync($"http://127.0.0.1:{_serverPort}/v1/chat/completions", content);
            }
            catch { }
        }

        public async IAsyncEnumerable<string> StreamPromptAsync(
            List<(string Role, string Content)> recentHistory,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
            string? systemPrompt = null)
        {
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = systemPrompt ?? "You are LockStepAI, a helpful Windows assistant."
                }
            };
            messages.AddRange(recentHistory
                .Where(message => message.Role == "user" || message.Role == "assistant")
                .Select(message => (object)new { role = message.Role, content = message.Content }));

            var payload = new
            {
                messages,
                n_predict = 512,
                temperature = 0.7,
                stream = true
            };

            var json = JsonSerializer.Serialize(payload);

            // Use dynamic port
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_serverPort}/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _inferenceClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
                var data = line.Substring(6).Trim();
                if (data == "[DONE]") yield break;

                string? token = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var c)) token = c.GetString();
                    }
                }
                catch { }

                if (!string.IsNullOrEmpty(token)) yield return token;
            }
        }

        private void StopServerProcess()
        {
            if (_serverProcess == null) return;

            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.WaitForExit();
                    LogStartup("Stopped llama-server after startup cancellation.");
                }
            }
            catch (InvalidOperationException)
            {
                // The process may exit between checking HasExited and killing it.
            }
            catch (Win32Exception ex)
            {
                StartupDiagnostic += $"{Environment.NewLine}Could not stop llama-server: {ex.Message}";
                LogStartup($"Could not stop llama-server after cancellation: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited) _serverProcess.Kill(true);
                _healthClient.Dispose();
                _inferenceClient.Dispose();
            }
            catch { }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LockStepAI.Services
{
    public class InferenceService : IDisposable
    {
        private Process? _serverProcess;
        private const int ServerPort = 8080;

        private readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
        private readonly HttpClient _inferenceClient = new() { Timeout = TimeSpan.FromMinutes(5) };

        public async Task<bool> StartAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string serverExe = Path.Combine(baseDir, "BitNet", "llama-server.exe");
            string modelPath = Path.Combine(baseDir, "BitNet", "ggml-model-i2_s.gguf");

            if (!File.Exists(serverExe) || !File.Exists(modelPath))
            {
                MessageBoxShow($"Missing files in BitNet folder.\nExe: {File.Exists(serverExe)}\nModel: {File.Exists(modelPath)}");
                return false;
            }

            try
            {
                // NOTICE: No -t flag. We let llama-server auto-detect the best cores.
                var startInfo = new ProcessStartInfo
                {
                    FileName = serverExe,
                    Arguments = $"-m \"{modelPath}\" -c 4096 --port {ServerPort}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.Combine(baseDir, "BitNet")
                };

                _serverProcess = new Process { StartInfo = startInfo };

                // Log server output to Visual Studio Output window just in case
                _serverProcess.OutputDataReceived += (s, e) => { if (e.Data != null) Debug.WriteLine($"[llama] {e.Data}"); };
                _serverProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) Debug.WriteLine($"[llama-err] {e.Data}"); };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                return await WaitForServerAsync(60);
            }
            catch (Exception ex)
            {
                MessageBoxShow($"Failed to launch llama-server: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> WaitForServerAsync(int timeoutSeconds)
        {
            for (int i = 0; i < timeoutSeconds; i++)
            {
                try
                {
                    var response = await _healthClient.GetAsync($"http://127.0.0.1:{ServerPort}/health");
                    if (response.IsSuccessStatusCode) return true;
                }
                catch { }
                await Task.Delay(1000);
            }
            return false;
        }

        public async Task WarmupAsync()
        {
            try
            {
                // Send a tiny hidden prompt to force the model into RAM
                var payload = new { messages = new[] { new { role = "user", content = "hi" } }, n_predict = 1, stream = false };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await _inferenceClient.PostAsync($"http://127.0.0.1:{ServerPort}/v1/chat/completions", content);
            }
            catch { }
        }

        public async IAsyncEnumerable<string> StreamPromptAsync(string prompt)
        {
            var payload = new
            {
                messages = new[]
                {
                    new { role = "system", content = "You are LockStep AI, a helpful Windows assistant. Keep your replies conversational, direct, and concise." },
                    new { role = "user", content = prompt }
                },
                n_predict = 512,
                temperature = 0.7,
                stream = true
            };

            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{ServerPort}/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _inferenceClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

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

        private void MessageBoxShow(string msg)
        {
            System.Windows.MessageBox.Show(msg, "Inference Service Error");
        }

        public void Dispose()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                    _serverProcess.Kill(true);

                _healthClient.Dispose();
                _inferenceClient.Dispose();
            }
            catch { }
        }
    }
}
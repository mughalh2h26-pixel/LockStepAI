using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LockStepAI.Services
{
    public static class ModelDownloader
    {
        private const string ModelUrl = "https://huggingface.co/microsoft/bitnet-b1.58-2B-4T-gguf/resolve/main/ggml-model-i2_s.gguf";
        private const long ExpectedSize = 1187801280;
        private const string ExpectedSha256 = "4221B252FDD5FD25E15847ADFEB5EE88886506BA50B8A34548374492884C2162";
        private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

        public static string ModelPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LockStepAI",
            "Models",
            "ggml-model-i2_s.gguf");

        public static async Task EnsureModelAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (File.Exists(ModelPath) && new FileInfo(ModelPath).Length == ExpectedSize)
                return;

            string directory = Path.GetDirectoryName(ModelPath)!;
            string temporaryPath = ModelPath + ".download";
            Directory.CreateDirectory(directory);

            try
            {
                using var response = await Client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] buffer = new byte[1024 * 1024];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        hash.AppendData(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        progress.Report(Math.Min(100, totalRead * 100d / ExpectedSize));
                    }

                    if (totalRead != ExpectedSize)
                        throw new InvalidDataException($"Model download size mismatch. Expected {ExpectedSize:N0} bytes but received {totalRead:N0} bytes.");

                    string actualHash = Convert.ToHexString(hash.GetHashAndReset());
                    if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded model failed its SHA-256 integrity check.");
                }

                File.Move(temporaryPath, ModelPath, true);
                progress.Report(100);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
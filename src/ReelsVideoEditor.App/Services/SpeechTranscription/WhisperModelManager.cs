using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ReelsVideoEditor.App.Services.SpeechTranscription;

public sealed class WhisperModelManager
{
    public string ModelName { get; set; } = "base";
    private string ModelFileName => $"ggml-{ModelName}.bin";
    private string ModelDownloadUrl => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{ModelFileName}";

    private static readonly string ModelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ReelsVideoEditor",
        "whisper-models");

    public string ModelPath => Path.Combine(ModelsDirectory, ModelFileName);

    public bool IsModelAvailable => File.Exists(ModelPath);

    public async Task EnsureModelAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsModelAvailable)
        {
            progress?.Report(100);
            return;
        }

        Directory.CreateDirectory(ModelsDirectory);

        var tempPath = ModelPath + ".download";

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);

            using var response = await httpClient.GetAsync(
                ModelDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloadedBytes = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (downloadedBytes / (double)totalBytes) * 100.0;
                    progress?.Report(Math.Min(percent, 99.0));
                }
            }

            await fileStream.FlushAsync(cancellationToken);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            throw;
        }

        File.Move(tempPath, ModelPath, overwrite: true);
        progress?.Report(100);
    }
}

using System.IO;
using System.Threading;

namespace Amaranth10API.Helpers;

public static class MailAttachmentFile
{
    public static string SuggestedName(string name)
    {
        // 메일의 파일명으로 저장 폴더를 지정하거나 벗어나지 않도록 이름 부분만 사용합니다.
        string file = name.Replace('\\', '/').Split('/').Last();
        foreach (char invalid in Path.GetInvalidFileNameChars()) file = file.Replace(invalid, '_');
        file = file.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(file)) file = "첨부파일";
        string stem = file.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) file = "_" + file;
        if (file.Length > 180)
        {
            string extension = Path.GetExtension(file);
            if (extension.Length > 20) extension = "";
            file = file.Substring(0, 180 - extension.Length) + extension;
        }
        return file;
    }

    public static string AvailablePath(string folder, string name)
    {
        string file = SuggestedName(name);
        string stem = Path.GetFileNameWithoutExtension(file);
        string extension = Path.GetExtension(file);
        string path = Path.Combine(Path.GetFullPath(folder), file);
        for (int number = 1; File.Exists(path) || Directory.Exists(path); number++)
            path = Path.Combine(Path.GetFullPath(folder), $"{stem} ({number}){extension}");
        return path;
    }

    public static async Task SaveAsync(Stream source, string destination, long? expectedLength,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default, bool overwrite = true)
    {
        string fullPath = Path.GetFullPath(destination);
        string temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".mail-download-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            long received = 0;
            var progressClock = System.Diagnostics.Stopwatch.StartNew();
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    received += read;
                    if (progressClock.ElapsedMilliseconds >= 100)
                    {
                        progress?.Report(received);
                        progressClock.Restart();
                    }
                }
                if (expectedLength.HasValue && received != expectedLength.Value) throw new IOException("첨부파일을 끝까지 받지 못했습니다. 다시 시도해 주세요.");
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (overwrite && File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
            else File.Move(temporary, fullPath);
            progress?.Report(received);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

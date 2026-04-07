using System.Text;

namespace Engine.MessageStorage;

public static class SafeFileWriter
{
    public static async Task AppendLinesAsync(string targetFile, IEnumerable<string> lines, CancellationToken ct)
    {
        if (!lines.Any()) return;
        Directory.CreateDirectory(Directory.GetParent(targetFile)!.FullName);

        var tempFile = targetFile + ".tmp";
        if (File.Exists(targetFile))
        {
            File.Copy(targetFile, tempFile);
        }
        await File.AppendAllLinesAsync(tempFile, lines, Encoding.UTF8, ct);
        File.Move(tempFile, targetFile, overwrite: true);
    }

    public static async Task WriteLinesAsync(string targetFile, IEnumerable<string> lines, CancellationToken ct)
    {
        if (!lines.Any())
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
            return;
        }
        Directory.CreateDirectory(Directory.GetParent(targetFile)!.FullName);

        var tempFile = targetFile + ".tmp";
        await File.WriteAllLinesAsync(tempFile, lines, Encoding.UTF8, ct);
        File.Move(tempFile, targetFile, overwrite: true);
    }
}
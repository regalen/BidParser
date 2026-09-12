namespace BidParser.Infrastructure.Storage;

/// <summary>
/// Filesystem storage for uploads and generated outputs under the configured upload dir (originals/ and
/// outputs/ subfolders). Each original is isolated in a GUID-named directory while retaining its safe
/// basename: parsers use the basename as a documented best-effort metadata fallback. <see cref="SaveUploadAsync"/>
/// streams with a byte cap and cleans up on failure. <see cref="TryDelete"/> never throws.
/// </summary>
public sealed class FileStorage
{
    private readonly string _uploadDir;

    public FileStorage(string uploadDir)
    {
        _uploadDir = uploadDir;
    }

    public string NewOriginalPath(string displayFilename)
    {
        var ext = Path.GetExtension(displayFilename).ToLowerInvariant();
        var dir = Path.Combine(_uploadDir, "originals", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var filename = Path.GetFileName(displayFilename);
        return Path.Combine(dir, filename.Length == 0 ? $"upload{ext}" : filename);
    }

    public string NewOutputPath(string extension = ".xlsx")
    {
        var dir = Path.Combine(_uploadDir, "outputs");
        Directory.CreateDirectory(dir);
        var normalisedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        return Path.Combine(dir, $"{Guid.NewGuid():N}{normalisedExtension}");
    }

    public async Task SaveUploadAsync(Stream source, string destPath, long maxBytes, CancellationToken ct = default)
    {
        var total = 0L;
        try
        {
            await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new UploadTooLargeException();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            TryDelete(destPath);
            throw;
        }
    }

    public void TryDelete(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            try { File.Delete(path); } catch { }
        }
    }
}

/// <summary>Thrown when a streamed upload exceeds the configured byte cap.</summary>
public sealed class UploadTooLargeException : Exception { }

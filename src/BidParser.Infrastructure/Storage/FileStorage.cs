namespace BidParser.Infrastructure.Storage;

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
        var dir = Path.Combine(_uploadDir, "originals");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
    }

    public string NewOutputPath()
    {
        var dir = Path.Combine(_uploadDir, "outputs");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid():N}.xlsx");
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

public sealed class UploadTooLargeException : Exception { }

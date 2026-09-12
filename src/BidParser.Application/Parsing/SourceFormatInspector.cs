namespace BidParser.Application.Parsing;

/// <summary>Canonical extension/MIME mapping and container signature validation.</summary>
public sealed class SourceFormatInspector
{
    public const string PdfMime = "application/pdf";
    public const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string XlsMime = "application/vnd.ms-excel";
    public const string JsonMime = "application/json";

    /// <summary>
    /// Largest quote a local host accepts. The web host configures its own limit from
    /// <c>MaxUploadBytes</c>; this is the compiled-in ceiling for hosts without configuration.
    /// </summary>
    public const long MaxSourceBytes = 10L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> ExtensionToMime =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = PdfMime,
            [".xlsx"] = XlsxMime,
            [".xls"] = XlsMime,
            [".json"] = JsonMime
        };

    public string ResolveMime(string filename)
    {
        var extension = Path.GetExtension(filename);
        return ExtensionToMime.TryGetValue(extension, out var mime)
            ? mime
            : throw new ParseInputException(
                ParseInputErrorKind.UnsupportedExtension,
                "Only PDF, XLS, XLSX, and JSON files are supported.");
    }

    public static IReadOnlyList<string> ExtensionsFor(IEnumerable<string> mimes)
    {
        var accepted = new HashSet<string>(mimes, StringComparer.OrdinalIgnoreCase);
        return ExtensionToMime
            .Where(pair => accepted.Contains(pair.Value))
            .Select(pair => pair.Key)
            .ToList();
    }

    public async Task ValidateMagicBytesAsync(string path, string acceptedMime, CancellationToken ct = default)
    {
        var header = new byte[512];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = await stream.ReadAsync(header, ct);

        var matches = acceptedMime switch
        {
            PdfMime => HasPrefix(header, bytesRead, [0x25, 0x50, 0x44, 0x46]),
            XlsxMime => HasPrefix(header, bytesRead, [0x50, 0x4B, 0x03, 0x04]),
            XlsMime => HasPrefix(header, bytesRead, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])
                       || FirstNonWhitespaceIs(header, bytesRead, (byte)'<'),
            JsonMime => FirstNonWhitespaceIs(header, bytesRead, (byte)'{', (byte)'[', skipUtf8Bom: true),
            _ => false
        };

        if (!matches)
        {
            throw new ParseInputException(
                ParseInputErrorKind.MagicByteMismatch,
                "Unsupported file format.",
                stage: "upload");
        }
    }

    private static bool HasPrefix(byte[] bytes, int length, ReadOnlySpan<byte> prefix)
        => length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static bool FirstNonWhitespaceIs(
        byte[] bytes,
        int length,
        byte first,
        byte? second = null,
        bool skipUtf8Bom = false)
    {
        var offset = skipUtf8Bom && length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? 3
            : 0;

        for (var index = offset; index < length; index++)
        {
            if (bytes[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return bytes[index] == first || (second is not null && bytes[index] == second.Value);
        }

        return false;
    }
}

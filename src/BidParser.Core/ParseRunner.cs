using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Output;
using BidParser.Parsing.Registry;

namespace BidParser.Core;

public sealed class ParseRunner
{
    private const double WrongFileTypeConfidence = 0.7;

    private static readonly Dictionary<string, string> ExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel"
    };

    private readonly IParserRegistry _registry = new ParserRegistry();

    public ParseOutcome Run(
        string inputPath,
        string vendor,
        string parserSlug,
        decimal? fxRate,
        decimal? margin,
        decimal? imPercent,
        decimal? onCostPercent,
        string? crmTemplate)
    {
        var parser = ResolveParser(parserSlug, vendor);
        ValidateExtension(inputPath, parser.AcceptedMime);
        ValidateMagicBytes(inputPath, parser.AcceptedMime);

        var effectiveFxRate = fxRate ?? 1m;
        var effectiveMargin = margin ?? 0m;

        try
        {
            var result = parser.Parse(inputPath);

            var template = string.IsNullOrEmpty(crmTemplate) ? parser.CrmTemplate : crmTemplate;
            if (!parser.AvailableTemplates.Contains(template))
                throw new ParseValidationException("Unknown CRM template for this parser.");

            var outputPath = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                OutputNaming.OutputFilename(Path.GetFileName(inputPath)));

            switch (template)
            {
                case CrmTemplates.ForeignUplift:
                    ForeignUpliftWriter.WriteForeignUplift(
                        result.LineItems, outputPath, effectiveMargin, effectiveFxRate,
                        vendor.ToUpperInvariant(), result.Metadata.Currency, parser.Slug);
                    break;
                case CrmTemplates.NoCalculation:
                    AnzGenericWriter.Write(
                        result.LineItems, outputPath, "No Calculation",
                        includeMargin: false, effectiveMargin, vendor.ToUpperInvariant(),
                        onCost: onCostPercent);
                    break;
                case CrmTemplates.Uplift:
                    AnzGenericWriter.Write(
                        result.LineItems, outputPath, "Uplift",
                        includeMargin: true, effectiveMargin, vendor.ToUpperInvariant(),
                        onCost: onCostPercent);
                    break;
                case CrmTemplates.PercentOffWithUplift:
                    if (imPercent is null)
                        throw new ParseValidationException("Discount Off MSRP (IM%) is required for this parser.");
                    PercentOffWithUpliftWriter.Write(
                        result.LineItems, outputPath, effectiveMargin, imPercent.Value, vendor.ToUpperInvariant());
                    break;
                default:
                    throw new ParseValidationException("Unsupported CRM template.");
            }

            var cancelledLines = result.LineItems
                .Where(i => i.IsCancelled)
                .Select(i => new CancelledLine(i.LineSequence ?? string.Empty, i.Vpn))
                .ToList();

            return new ParseOutcome(result.Validation, result.Metadata.Currency, cancelledLines, outputPath);
        }
        catch (ParseError pe) when (pe.Stage == "detect")
        {
            var suggestedType = DetectSuggestedType(parser, inputPath);
            var message = suggestedType is null
                ? $"The file is not recognised as {parser.DisplayName}. Check the selected file type and try again."
                : $"The file is not recognised as {parser.DisplayName} and appears to be a {suggestedType}. Select the correct file type and try again.";
            throw new ParseError("file_type", message, message);
        }
    }

    private string? DetectSuggestedType(IParser selected, string path)
    {
        IParser? best = null;
        var bestScore = 0.0;

        foreach (var candidate in _registry.Parsers)
        {
            if (candidate.Slug == selected.Slug
                || candidate.Vendor != selected.Vendor
                || candidate.AcceptedMime != selected.AcceptedMime)
            {
                continue;
            }

            var score = candidate.Detect(path);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return bestScore >= WrongFileTypeConfidence ? best!.DisplayName : null;
    }

    private IParser ResolveParser(string parserSlug, string vendor)
    {
        var parser = _registry.Parsers.FirstOrDefault(p => p.Slug == parserSlug)
            ?? throw new ParseValidationException("Unknown parser.");
        if (parser.Vendor != vendor)
            throw new ParseValidationException("Parser does not match vendor.");
        return parser;
    }

    private static void ValidateExtension(string filename, string parserAcceptedMime)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (!ExtensionToMime.TryGetValue(ext, out var extensionMime))
            throw new ParseValidationException("Only PDF, XLS, and XLSX files are supported.");
        if (extensionMime != parserAcceptedMime)
            throw new ParseValidationException("File extension does not match selected parser.");
    }

    private static void ValidateMagicBytes(string path, string parserAcceptedMime)
    {
        var header = new byte[512];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = stream.Read(header, 0, header.Length);

        var matches = parserAcceptedMime switch
        {
            "application/pdf" => bytesRead >= 4
                && header[0] == 0x25 && header[1] == 0x50
                && header[2] == 0x44 && header[3] == 0x46,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => bytesRead >= 4
                && header[0] == 0x50 && header[1] == 0x4B
                && header[2] == 0x03 && header[3] == 0x04,
            "application/vnd.ms-excel" => IsOleCompoundDoc(header, bytesRead) || IsHtmlContent(header, bytesRead),
            _ => false
        };

        if (!matches)
            throw new ParseError("upload", "Unsupported file format.", "Unsupported file format.");
    }

    private static bool IsOleCompoundDoc(byte[] h, int n)
        => n >= 8
            && h[0] == 0xD0 && h[1] == 0xCF && h[2] == 0x11 && h[3] == 0xE0
            && h[4] == 0xA1 && h[5] == 0xB1 && h[6] == 0x1A && h[7] == 0xE1;

    private static bool IsHtmlContent(byte[] h, int n)
    {
        for (var i = 0; i < n; i++)
        {
            if (h[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            return h[i] == (byte)'<';
        }
        return false;
    }
}

public sealed record ParseOutcome(
    ValidationResult Validation,
    string Currency,
    IReadOnlyList<CancelledLine> CancelledLines,
    string OutputPath);

public sealed record CancelledLine(string Line, string Vpn);

public sealed class ParseValidationException(string message) : Exception(message);

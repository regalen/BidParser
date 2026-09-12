using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;

namespace BidParser.Application.Parsing;

public sealed record QuoteParseRequest(
    string SourcePath,
    string Vendor,
    string ParserSlug,
    ParseOptions? Options = null,
    string? SourceFilename = null);

public sealed record QuoteParseSelection(
    string Vendor,
    string ParserSlug,
    string AcceptedMime,
    IParser? Parser,
    bool IsAuto);

public sealed record ParsedQuote(
    string SourcePath,
    string SourceFilename,
    IParser Parser,
    ParseResult Result,
    bool WasAuto);

/// <summary>Local, host-neutral parser selection, validation, detection, and invocation.</summary>
public sealed class QuoteParseService(IParserRegistry registry, SourceFormatInspector sourceInspector)
{
    public QuoteParseSelection ResolveSelection(string filename, string vendor, string parserSlug)
    {
        var mime = sourceInspector.ResolveMime(filename);
        if (AutoDetectTypes.BySlug(parserSlug) is { } auto)
        {
            if (auto.Vendor != vendor)
            {
                throw new ParseInputException(ParseInputErrorKind.VendorMismatch, "Parser does not match vendor.");
            }

            return new QuoteParseSelection(vendor, parserSlug, mime, Parser: null, IsAuto: true);
        }

        var parser = registry.Parsers.FirstOrDefault(candidate => candidate.Slug == parserSlug)
            ?? throw new ParseInputException(ParseInputErrorKind.UnknownParser, "Unknown parser.");
        if (parser.Vendor != vendor)
        {
            throw new ParseInputException(ParseInputErrorKind.VendorMismatch, "Parser does not match vendor.");
        }
        if (!parser.AcceptedMimes.Contains(mime, StringComparer.OrdinalIgnoreCase))
        {
            throw new ParseInputException(ParseInputErrorKind.ExtensionMismatch, "File extension does not match selected parser.");
        }

        return new QuoteParseSelection(vendor, parserSlug, mime, parser, IsAuto: false);
    }

    public Task<ParsedQuote> ParseAsync(QuoteParseRequest request, CancellationToken ct = default)
        => ParseAsync(request, ResolveSelection(request.SourcePath, request.Vendor, request.ParserSlug), ct);

    public async Task<ParsedQuote> ParseAsync(
        QuoteParseRequest request,
        QuoteParseSelection selection,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await sourceInspector.ValidateMagicBytesAsync(request.SourcePath, selection.AcceptedMime, ct);
        ct.ThrowIfCancellationRequested();

        var parser = selection.Parser
            ?? FormatDetection.Resolve(registry.Parsers, request.Vendor, selection.AcceptedMime, request.SourcePath)
            ?? throw new ParseInputException(
                ParseInputErrorKind.AutoDetectionFailed,
                AutoDetectTypes.NoMatchMessage,
                stage: "fileType");

        try
        {
            var result = parser.Parse(request.SourcePath, request.Options ?? new ParseOptions());
            ct.ThrowIfCancellationRequested();
            return new ParsedQuote(
                request.SourcePath,
                request.SourceFilename ?? Path.GetFileName(request.SourcePath),
                parser,
                result,
                selection.IsAuto);
        }
        catch (ParseError error) when (error.Stage == "detect")
        {
            var suggestion = selection.IsAuto
                ? null
                : FormatDetection.Resolve(
                    registry.Parsers,
                    parser.Vendor,
                    selection.AcceptedMime,
                    request.SourcePath,
                    exclude: parser)?.DisplayName;

            var message = selection.IsAuto
                ? AutoDetectTypes.DetectedButFailed(parser.DisplayName)
                : suggestion is null
                    ? $"The file is not recognised as {parser.DisplayName}. Check the selected file type and try again."
                    : $"The file is not recognised as {parser.DisplayName} and appears to be a {suggestion}. Select the correct file type and try again.";

            throw new ParseInputException(
                ParseInputErrorKind.WrongFileType,
                message,
                stage: "fileType",
                suggestedParserName: suggestion);
        }
    }
}

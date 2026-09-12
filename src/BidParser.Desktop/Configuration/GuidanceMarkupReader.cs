using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BidParser.Desktop.Configuration;

/// <summary>
/// Converts the schema's tiny markup subset into a <see cref="GuidanceDocument"/>. Anything outside
/// the subset — any other element, any attribute, any namespace, any nesting the schema does not
/// describe, a DTD, a processing instruction, a CDATA section — rejects the whole message.
/// <para>
/// Rejection is deliberately total rather than best-effort: a partially understood document from a
/// public source is exactly the input that should not be rendered.
/// </para>
/// </summary>
public static partial class GuidanceMarkupReader
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        // No DTD means no entity expansion, no external subset, and no billion-laughs.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = false,
        CheckCharacters = true
    };

    /// <returns>The converted document, or null when the markup must be rejected.</returns>
    public static GuidanceDocument? TryRead(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return null;
        }

        XElement root;
        try
        {
            // A synthetic root lets a message be a sequence of blocks, or bare text, without
            // requiring the author to wrap it.
            using var text = new StringReader($"<guidance>{markup}</guidance>");
            using var reader = XmlReader.Create(text, ReaderSettings);
            var document = XDocument.Load(reader);
            root = document.Root!;
        }
        catch (XmlException)
        {
            return null;
        }

        return ReadBlocks(root);
    }

    private static GuidanceDocument? ReadBlocks(XElement root)
    {
        var blocks = new List<GuidanceBlock>();
        var looseText = new List<GuidanceRun>();

        foreach (var node in root.Nodes())
        {
            switch (node)
            {
                case XText text:
                    // Bare text between blocks is plain text, which the schema allows.
                    var run = Collapse(text.Value);
                    if (run.Length > 0)
                    {
                        looseText.Add(new GuidanceRun(run, IsStrong: false));
                    }
                    break;

                case XElement element when IsTag(element, "p"):
                    FlushLooseText(looseText, blocks);
                    if (ReadRuns(element) is not { } paragraph)
                    {
                        return null;
                    }
                    blocks.Add(new GuidanceParagraph(paragraph));
                    break;

                case XElement element when IsTag(element, "ul"):
                    FlushLooseText(looseText, blocks);
                    if (ReadListItems(element) is not { } items)
                    {
                        return null;
                    }
                    blocks.Add(new GuidanceBulletList(items));
                    break;

                default:
                    return null;
            }
        }

        FlushLooseText(looseText, blocks);
        return blocks.Count == 0 ? null : new GuidanceDocument(blocks);
    }

    private static IReadOnlyList<GuidanceListItem>? ReadListItems(XElement list)
    {
        if (!IsPlain(list))
        {
            return null;
        }

        var items = new List<GuidanceListItem>();
        foreach (var node in list.Nodes())
        {
            switch (node)
            {
                case XText text when Collapse(text.Value).Length == 0:
                    break;

                case XElement element when IsTag(element, "li"):
                    if (ReadRuns(element) is not { } runs)
                    {
                        return null;
                    }
                    items.Add(new GuidanceListItem(runs));
                    break;

                default:
                    return null;
            }
        }

        return items.Count == 0 ? null : items;
    }

    private static IReadOnlyList<GuidanceRun>? ReadRuns(XElement container)
    {
        if (!IsPlain(container))
        {
            return null;
        }

        var runs = new List<GuidanceRun>();
        foreach (var node in container.Nodes())
        {
            switch (node)
            {
                case XText text:
                    Append(runs, Collapse(text.Value), isStrong: false);
                    break;

                case XElement element when IsTag(element, "strong") || IsTag(element, "b"):
                    // Emphasis carries text only; the schema has no nested formatting.
                    if (!IsPlain(element) || element.Elements().Any())
                    {
                        return null;
                    }
                    Append(runs, Collapse(element.Value), isStrong: true);
                    break;

                default:
                    return null;
            }
        }

        return Trim(runs) is { Count: > 0 } trimmed ? trimmed : null;
    }

    private static void Append(List<GuidanceRun> runs, string text, bool isStrong)
    {
        if (text.Length > 0)
        {
            runs.Add(new GuidanceRun(text, isStrong));
        }
    }

    private static void FlushLooseText(List<GuidanceRun> looseText, List<GuidanceBlock> blocks)
    {
        if (Trim(looseText) is { Count: > 0 } runs)
        {
            blocks.Add(new GuidanceParagraph(runs));
        }

        looseText.Clear();
    }

    private static List<GuidanceRun> Trim(List<GuidanceRun> runs)
    {
        var trimmed = new List<GuidanceRun>(runs);
        if (trimmed.Count > 0)
        {
            trimmed[0] = trimmed[0] with { Text = trimmed[0].Text.TrimStart() };
            var last = trimmed.Count - 1;
            trimmed[last] = trimmed[last] with { Text = trimmed[last].Text.TrimEnd() };
        }

        trimmed.RemoveAll(run => run.Text.Length == 0);
        return trimmed;
    }

    /// <summary>An unprefixed element of the given name carrying no attributes and no namespace.</summary>
    private static bool IsTag(XElement element, string name)
        => IsPlain(element) && element.Name.LocalName == name;

    private static bool IsPlain(XElement element)
        => element.Name.Namespace == XNamespace.None && !element.HasAttributes;

    /// <summary>Collapses every whitespace run to one space, the way the source markup reads.</summary>
    private static string Collapse(string value) => WhitespaceRun().Replace(value, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}

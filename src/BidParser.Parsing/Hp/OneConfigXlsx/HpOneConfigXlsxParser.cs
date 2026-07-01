using BidParser.Domain.Abstractions;
using BidParser.Domain.Constants;
using BidParser.Domain.Models;
using BidParser.Parsing.Cleaning;
using BidParser.Parsing.Xlsx;
using ClosedXML.Excel;

namespace BidParser.Parsing.Hp.OneConfigXlsx;

public sealed class HpOneConfigXlsxParser : IParser
{
    public string Slug => ParserSlugs.HpOneConfigXlsx;
    public string DisplayName => "OneConfig (XLSX)";
    public string Vendor => Vendors.Hp;
    public string AcceptedMime => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string CrmTemplate => CrmTemplates.PercentOffWithUplift;

    // Signature: the "Config ID" header, unique to the HP OneConfig export.
    public double Detect(string path)
    {
        try
        {
            using var workbook = WorkbookReader.Open(path);
            var sheet = workbook.Worksheets.First();
            return WorkbookReader.FindCell(sheet, "Config ID") is not null ? 0.9 : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }

    public ParseResult Parse(string path)
    {
        using var workbook = WorkbookReader.Open(path);
        var sheet = workbook.Worksheets.First();

        // Locate the Config header row by anchor label "Config ID"
        var configHeaderCell = WorkbookReader.FindCell(sheet, "Config ID")
            ?? throw new ParseError("detect", "Could not find Config ID header.", "Could not find Config ID header.");

        var configHeaderRow = configHeaderCell.Address.RowNumber;
        var configIdColumn = configHeaderCell.Address.ColumnNumber;
        var configHeaderMap = WorkbookReader.HeaderMap(sheet, configHeaderRow);
        WorkbookReader.RequireLabels(configHeaderMap, "Config ID", "Config Name", "Total Price");

        // The Config data row is immediately below the header
        var configDataRow = configHeaderRow + 1;

        var configId = WorkbookReader.CellText(sheet.Cell(configDataRow, configHeaderMap.Require("Config ID")));
        var configName = WorkbookReader.CellText(sheet.Cell(configDataRow, configHeaderMap.Require("Config Name")));
        var totalPriceText = WorkbookReader.CellText(sheet.Cell(configDataRow, configHeaderMap.Require("Total Price")));

        if (configId.Length == 0)
        {
            throw new ParseError("config", "Config ID is empty.", "OneConfig workbook must contain a non-empty Config ID.");
        }

        var msrp = DecimalCleaner.Parse(totalPriceText);

        // Guard against multi-config workbooks: a second "Config ID" header below the
        // current one in the *same column* indicates an unsupported multi-config file.
        // Scoping to the column avoids false positives on component descriptions that
        // happen to contain the literal text "Config ID".
        if (WorkbookReader.FindCellAfterInColumn(sheet, "Config ID", configHeaderRow, configIdColumn) is not null)
        {
            throw new ParseError("config", "OneConfig must contain exactly one Config ID row.", "More than one Config ID row found.");
        }

        // Locate the Components header row by anchor label "Part Number" (below the Config row)
        var partNumberHeaderCell = WorkbookReader.FindCellAfter(sheet, "Part Number", configDataRow)
            ?? throw new ParseError("components", "Could not find Part Number header.", "Could not find Part Number header in component section.");

        var componentsHeaderRow = partNumberHeaderCell.Address.RowNumber;
        var componentsHeaderMap = WorkbookReader.HeaderMap(sheet, componentsHeaderRow);
        WorkbookReader.RequireLabels(componentsHeaderMap, "Part Number", "Description", "Quantity");

        var items = new List<LineItem>();

        // Every emitted line takes the next number in a single running sequence: the Config
        // (parent) row is 1, and its components follow as 2, 3, 4, … rather than being
        // sub-numbered under the parent (1.01, 1.02, …).
        var lineCounter = 1;

        // Parent (Config) row
        items.Add(new LineItem
        {
            LineSequence = lineCounter.ToString(),
            Vpn = configId,
            Description = configName,
            Qty = 1,
            Msrp = msrp,
            Cost = 0m,
            Raw = WorkbookReader.BuildRawDict(sheet, configDataRow, configHeaderMap)
        });

        // Child (component) rows
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? componentsHeaderRow;
        for (var row = componentsHeaderRow + 1; row <= lastRow; row++)
        {
            if (WorkbookReader.RowIsEmpty(sheet, row))
            {
                break;
            }

            var partNumber = WorkbookReader.CellText(sheet.Cell(row, componentsHeaderMap.Require("Part Number")));
            if (partNumber.Length == 0)
            {
                continue;
            }

            lineCounter++;
            var description = WorkbookReader.CellText(sheet.Cell(row, componentsHeaderMap.Require("Description")));
            var qtyText = WorkbookReader.CellText(sheet.Cell(row, componentsHeaderMap.Require("Quantity")));
            var qty = DecimalCleaner.ParseOptionalInt(qtyText) ?? 1;

            items.Add(new LineItem
            {
                LineSequence = lineCounter.ToString(),
                Vpn = partNumber,
                Description = description,
                Qty = qty,
                Msrp = 0m,
                Cost = 0m,
                Raw = WorkbookReader.BuildRawDict(sheet, row, componentsHeaderMap)
            });
        }

        var validation = new ValidationResult
        {
            ComputedTotal = 0m,
            QuotedTotal = null,
            Matches = true,
            Difference = 0m
        };

        return new ParseResult
        {
            Metadata = new QuoteMetadata
            {
                QuoteNumber = configId,
                Supplier = Vendor,
                Currency = "AUD",
                QuotedTotal = null,
                SourceFilename = Path.GetFileName(path),
                ParserSlug = Slug
            },
            LineItems = items,
            Validation = validation
        };
    }

}

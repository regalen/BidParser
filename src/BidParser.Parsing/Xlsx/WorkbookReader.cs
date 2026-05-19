using BidParser.Parsing.Cleaning;
using ClosedXML.Excel;

namespace BidParser.Parsing.Xlsx;

public static class WorkbookReader
{
    public static XLWorkbook Open(string path) => new XLWorkbook(path);

    public static IXLCell? FindCell(IXLWorksheet sheet, string expected)
    {
        return UsedCells(sheet).FirstOrDefault(cell => CellText(cell) == expected);
    }

    public static IXLCell? FindCellStarting(IXLWorksheet sheet, string prefix)
    {
        return UsedCells(sheet).FirstOrDefault(cell => CellText(cell).StartsWith(prefix, StringComparison.Ordinal));
    }

    public static HeaderMap HeaderMap(IXLWorksheet sheet, int rowNumber)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var labels = new Dictionary<string, int>();
        for (var column = 1; column <= lastColumn; column++)
        {
            var label = CellText(sheet.Cell(rowNumber, column));
            if (label.Length > 0)
            {
                labels[label] = column;
            }
        }

        return new HeaderMap(rowNumber, labels);
    }

    public static void RequireLabels(HeaderMap headerMap, params string[] labels)
    {
        foreach (var label in labels)
        {
            _ = headerMap.Require(label);
        }
    }

    public static bool RowIsEmpty(IXLWorksheet sheet, int rowNumber)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var column = 1; column <= lastColumn; column++)
        {
            if (CellText(sheet.Cell(rowNumber, column)).Length > 0)
            {
                return false;
            }
        }

        return true;
    }

    public static string CellText(IXLCell cell)
    {
        return TextCleaner.Clean(cell.GetFormattedString());
    }

    public static object? CellValue(IXLCell cell)
    {
        if (cell.Value.IsBlank)
        {
            return null;
        }

        return cell.Value;
    }

    public static decimal ParseTotalText(object? value)
    {
        var text = CellTextValue(value);
        if (text.StartsWith("TOTAL ", StringComparison.Ordinal))
        {
            text = text["TOTAL ".Length..];
        }

        return DecimalCleaner.Parse(text);
    }

    private static IEnumerable<IXLCell> UsedCells(IXLWorksheet sheet)
    {
        return sheet.RangeUsed()?.CellsUsed() ?? Enumerable.Empty<IXLCell>();
    }

    private static string CellTextValue(object? value)
    {
        return TextCleaner.Clean(value);
    }
}

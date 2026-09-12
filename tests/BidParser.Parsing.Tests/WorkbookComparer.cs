using ClosedXML.Excel;
using FluentAssertions;

namespace BidParser.Parsing.Tests;

public static class WorkbookComparer
{
    public static void AssertEqual(string actualPath, string expectedPath)
    {
        using var actual = new XLWorkbook(actualPath);
        using var expected = new XLWorkbook(expectedPath);

        actual.Worksheets.Select(sheet => sheet.Name).Should().Equal(expected.Worksheets.Select(sheet => sheet.Name));

        foreach (var expectedSheet in expected.Worksheets)
        {
            var actualSheet = actual.Worksheet(expectedSheet.Name);
            var expectedRange = expectedSheet.RangeUsed();
            var actualRange = actualSheet.RangeUsed();
            actualRange.Should().NotBeNull($"{expectedSheet.Name} should have a used range");
            actualRange!.RangeAddress.LastAddress.RowNumber.Should().Be(expectedRange!.RangeAddress.LastAddress.RowNumber);
            actualRange.RangeAddress.LastAddress.ColumnNumber.Should().Be(expectedRange.RangeAddress.LastAddress.ColumnNumber);

            for (var row = 1; row <= expectedRange.RangeAddress.LastAddress.RowNumber; row++)
            {
                for (var column = 1; column <= expectedRange.RangeAddress.LastAddress.ColumnNumber; column++)
                {
                    var actualCell = actualSheet.Cell(row, column);
                    var expectedCell = expectedSheet.Cell(row, column);
                    Normalize(actualCell.Value).Should().Be(Normalize(expectedCell.Value), $"{expectedSheet.Name}!{actualCell.Address}");
                    actualCell.Style.NumberFormat.Format.Should().Be(expectedCell.Style.NumberFormat.Format, $"{expectedSheet.Name}!{actualCell.Address} number format");
                }
            }
        }
    }

    private static object? Normalize(XLCellValue value)
    {
        if (value.IsBlank)
        {
            return null;
        }

        if (value.IsNumber)
        {
            var number = value.GetNumber();
            return Math.Abs(number - Math.Truncate(number)) < double.Epsilon ? Convert.ToDecimal(Math.Truncate(number)) : Convert.ToDecimal(number);
        }

        if (value.IsDateTime)
        {
            return value.GetDateTime();
        }

        if (value.IsText)
        {
            return value.GetText();
        }

        if (value.IsBoolean)
        {
            return value.GetBoolean();
        }

        return value.ToString();
    }
}

namespace BidParser.Domain.Constants;

public static class CrmTemplates
{
    public const string ForeignPercentOffWithUplift = "Foreign %Off RRP with Uplift";
    public const string ForeignUplift = "Foreign Uplift";
    public const string ForeignNoCalculation = "Foreign No Calculation";
    public const string NoCalculation = "No Calculation";
    public const string Uplift = "Uplift";
    public const string PercentOffWithUplift = "% Off RRP with Uplift";

    public static string FileToken(string template) => template switch
    {
        ForeignPercentOffWithUplift => "ForeignPercentOffWithUplift",
        ForeignUplift => "ForeignUplift",
        ForeignNoCalculation => "ForeignNoCalculation",
        NoCalculation => "NoCalculation",
        Uplift => "Uplift",
        PercentOffWithUplift => "PercentOffWithUplift",
        _ => "parsed",
    };
}

namespace BidParser.Domain.Constants;

public static class CrmTemplates
{
    public const string ForeignUplift = "Foreign Uplift";
    public const string NoCalculation = "No Calculation";
    public const string Uplift = "Uplift";
    public const string PercentOffWithUplift = "% Off RRP with Uplift";

    public static string FileToken(string template) => template switch
    {
        ForeignUplift => "ForeignUplift",
        NoCalculation => "NoCalculation",
        Uplift => "Uplift",
        PercentOffWithUplift => "PercentOffWithUplift",
        _ => "parsed",
    };
}

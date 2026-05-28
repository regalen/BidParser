namespace BidParser.Output;

internal static class TemplateLayout
{
    // Zero-dollar lines export as this sentinel because the downstream import rejects a literal 0
    // and rounds the sentinel back to 0. Applies to every writer's price columns.
    internal const decimal ZeroPriceSentinel = 0.0001m;

    internal static readonly string[] Headers =
    [
        "Item",
        "Vendor Name",
        "IMTH SKU\n(Optional)",
        "Vendor Part Number",
        "Description",
        "Qty.",
        "Unit Price",
        "MSRP",
        "Cost",
        "Discount",
        "Margin",
        "Product Part Number \n(for Warranty/Renewal)",
        "Serial Number",
        "Warranty / Duration (months)",
        "Vendor Ref",
        "Start Date",
        "End Date",
        "Comments",
        "Foreign Currency",
        "Foreign Cost",
        "Foreign MSRP",
        "Foreign Exchange Rate",
        "Min Order Qty",
        "IM%",
        "Diff%",
        "On Cost %",
        "Retail Bump %"
    ];
}

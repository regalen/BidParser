namespace BidParser.Domain.Constants;

/// <summary>How a format's output filenames are composed. A presentation trait of the format,
/// so the parser declares it and generic naming obeys — naming must never inspect a slug.</summary>
public enum OutputNameStyle
{
    /// <summary>"{bid}_{revision}_[{solution}_]{token}.xlsx"; falls back to the source
    /// filename stem when bid metadata is absent. The historical behaviour.</summary>
    BidScoped,

    /// <summary>"{bid}_{token}" for the whole-quote workbook and for the archive;
    /// "{solution}_{token}" for each workbook inside the archive. For formats where the
    /// split dimension, not the bid, is the identity that matters downstream.</summary>
    SolutionScoped,
}

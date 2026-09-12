namespace BidParser.Desktop.Configuration;

/// <summary>
/// Result guidance as a safe presentation model. Remote markup is converted to this during
/// validation and the markup itself is discarded, so nothing downstream can render or execute it.
/// </summary>
public sealed record GuidanceDocument(IReadOnlyList<GuidanceBlock> Blocks);

public abstract record GuidanceBlock;

public sealed record GuidanceParagraph(IReadOnlyList<GuidanceRun> Runs) : GuidanceBlock;

public sealed record GuidanceBulletList(IReadOnlyList<GuidanceListItem> Items) : GuidanceBlock;

public sealed record GuidanceListItem(IReadOnlyList<GuidanceRun> Runs);

/// <summary>One span of text, optionally emphasised. The only styling the schema can express.</summary>
public sealed record GuidanceRun(string Text, bool IsStrong);

namespace DevHub.Core.Models;

/// <summary>A commit DAG laid out into lanes for graph rendering.</summary>
public sealed record GitGraph
{
    public required IReadOnlyList<GitGraphRow> Rows { get; init; }

    /// <summary>Highest lane index used anywhere — determines the graph column width.</summary>
    public required int MaxLane { get; init; }
}

/// <summary>One commit row: the dot position plus the rail segments above and below it.</summary>
public sealed record GitGraphRow
{
    public required string Sha { get; init; }

    public required string ShortSha { get; init; }

    public required string Message { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset When { get; init; }

    /// <summary>Lane of this commit's dot.</summary>
    public required int Lane { get; init; }

    /// <summary>Rails in the row's top half: from lane (previous row) to lane (this row's center).</summary>
    public required IReadOnlyList<GitGraphSegment> TopSegments { get; init; }

    /// <summary>Rails in the row's bottom half: from lane (center) to lane (next row).</summary>
    public required IReadOnlyList<GitGraphSegment> BottomSegments { get; init; }

    /// <summary>Branch and tag names pointing at this commit.</summary>
    public IReadOnlyList<string> Refs { get; init; } = [];
}

public readonly record struct GitGraphSegment(int From, int To);

/// <summary>Builder input: a commit with its parent shas, in topological order (newest first).</summary>
public sealed record GitGraphCommit(
    string Sha,
    string Message,
    string Author,
    DateTimeOffset When,
    IReadOnlyList<string> Parents,
    IReadOnlyList<string> Refs);

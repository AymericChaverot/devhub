using DevHub.Core.Models;
using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class GitGraphBuilderTests
{
    private static GitGraphCommit Commit(string sha, string[] parents, string[]? refs = null) =>
        new(sha, $"commit {sha}", "Test", DateTimeOffset.Now, parents, refs ?? []);

    [Fact]
    public void LinearHistory_StaysOnLaneZero()
    {
        var graph = GitGraphBuilder.Build(
        [
            Commit("c3", ["c2"]),
            Commit("c2", ["c1"]),
            Commit("c1", []),
        ]);

        Assert.Equal(0, graph.MaxLane);
        Assert.All(graph.Rows, r => Assert.Equal(0, r.Lane));
        // The root commit has no parent, so nothing continues below it.
        Assert.Empty(graph.Rows[^1].BottomSegments);
    }

    [Fact]
    public void MergeCommit_BranchesOutAndConvergesBack()
    {
        // main:    M ─ A ─ R
        // feature:  └ B ──┘   (M merges B, both A and B descend from R)
        var graph = GitGraphBuilder.Build(
        [
            Commit("M", ["A", "B"]),
            Commit("A", ["R"]),
            Commit("B", ["R"]),
            Commit("R", []),
        ]);

        Assert.Equal(1, graph.MaxLane);

        var merge = graph.Rows[0];
        Assert.Equal(0, merge.Lane);
        Assert.Equal(2, merge.BottomSegments.Count);
        Assert.Contains(new GitGraphSegment(0, 0), merge.BottomSegments);
        Assert.Contains(new GitGraphSegment(0, 1), merge.BottomSegments);

        var b = graph.Rows[2];
        Assert.Equal(1, b.Lane);

        // Both lanes converge into the root's dot.
        var root = graph.Rows[3];
        Assert.Equal(0, root.Lane);
        Assert.Contains(new GitGraphSegment(0, 0), root.TopSegments);
        Assert.Contains(new GitGraphSegment(1, 0), root.TopSegments);
        Assert.Empty(root.BottomSegments);
    }

    [Fact]
    public void UnmergedBranchTip_OpensItsOwnLane()
    {
        // Two tips: main (c2 → c1) and feature (f1 → c1).
        var graph = GitGraphBuilder.Build(
        [
            Commit("c2", ["c1"], ["main"]),
            Commit("f1", ["c1"], ["feature"]),
            Commit("c1", []),
        ]);

        Assert.Equal(0, graph.Rows[0].Lane);
        Assert.Equal(1, graph.Rows[1].Lane);
        Assert.Equal(["main"], graph.Rows[0].Refs);
        // Both rails converge on the shared parent.
        Assert.Equal(2, graph.Rows[2].TopSegments.Count);
    }

    [Fact]
    public void PassThroughLane_ContinuesWhileOtherCommitsAreLaidOut()
    {
        // feature tip f2→f1 interleaved with main c2→c1; both roots independent.
        var graph = GitGraphBuilder.Build(
        [
            Commit("c2", ["c1"]),
            Commit("f2", ["f1"]),
            Commit("c1", []),
            Commit("f1", []),
        ]);

        // While c1 is emitted, the f-lane passes straight through.
        var c1Row = graph.Rows[2];
        Assert.Contains(new GitGraphSegment(1, 1), c1Row.TopSegments);
        Assert.Contains(new GitGraphSegment(1, 1), c1Row.BottomSegments);
    }

    [Fact]
    public void MergeParentReusingAJustFreedLane_DoesNotDrawTheOldRail()
    {
        // Two tips (A, C) both descend from merge commit M; when M is laid out,
        // C's lane (1) frees and M's second parent Q re-uses it. The old lane 1
        // rail must not leak into M's bottom half.
        var graph = GitGraphBuilder.Build(
        [
            Commit("A", ["M"]),
            Commit("C", ["M"]),
            Commit("M", ["P", "Q"]),
            Commit("P", []),
            Commit("Q", []),
        ]);

        var merge = graph.Rows[2];
        Assert.Equal(0, merge.Lane);
        Assert.Equal(2, merge.BottomSegments.Count);
        Assert.Contains(new GitGraphSegment(0, 0), merge.BottomSegments);
        Assert.Contains(new GitGraphSegment(0, 1), merge.BottomSegments);
        Assert.DoesNotContain(new GitGraphSegment(1, 1), merge.BottomSegments);
    }

    [Fact]
    public void MergeParentLanes_AllocateToTheRightOfTheDot()
    {
        // Lane 0 frees (root Z) while M is awaited on lane 1. M's second parent
        // must branch right (lane 2), never across the dot into freed lane 0.
        var graph = GitGraphBuilder.Build(
        [
            Commit("T0", ["Z"]),
            Commit("T1", ["M"]),
            Commit("Z", []),
            Commit("M", ["P", "Q"]),
            Commit("P", []),
            Commit("Q", []),
        ]);

        var merge = graph.Rows[3];
        Assert.Equal(1, merge.Lane);
        var branchOut = merge.BottomSegments.Single(s => s.From != s.To);
        Assert.True(branchOut.To > merge.Lane, "merge parent lane must open to the right of the dot");
    }

    [Fact]
    public void EmptyInput_YieldsEmptyGraph()
    {
        var graph = GitGraphBuilder.Build([]);

        Assert.Empty(graph.Rows);
        Assert.Equal(0, graph.MaxLane);
    }
}

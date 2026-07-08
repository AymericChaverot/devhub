using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>
/// Lays out a topologically ordered commit list (newest first) into graph lanes,
/// the way `git log --graph` or an IDE's commit tree does.
/// </summary>
public static class GitGraphBuilder
{
    public static GitGraph Build(IReadOnlyList<GitGraphCommit> commits)
    {
        // Each lane holds the sha it is waiting for (the parent expected further down).
        var lanes = new List<string?>();
        var rows = new List<GitGraphRow>(commits.Count);
        var maxLane = 0;

        foreach (var commit in commits)
        {
            var incoming = new List<int>();
            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == commit.Sha)
                {
                    incoming.Add(i);
                }
            }

            // The dot sits on the first lane waiting for this commit, or opens a new one (branch tip).
            int dot;
            if (incoming.Count > 0)
            {
                dot = incoming[0];
            }
            else
            {
                dot = lanes.IndexOf(null);
                if (dot < 0)
                {
                    lanes.Add(null);
                    dot = lanes.Count - 1;
                }
            }

            // Top half: every active lane continues straight, except those converging into the dot.
            var wasActive = new bool[lanes.Count];
            var top = new List<GitGraphSegment>();
            for (var i = 0; i < lanes.Count; i++)
            {
                wasActive[i] = lanes[i] is not null;
                if (lanes[i] is null)
                {
                    continue;
                }

                top.Add(new GitGraphSegment(i, lanes[i] == commit.Sha ? dot : i));
            }

            foreach (var i in incoming)
            {
                lanes[i] = null;
            }

            // Bottom half: the dot continues towards its first parent; extra parents (merges)
            // either join the lane already waiting for them or open a new one.
            var bottom = new List<GitGraphSegment>();
            var reusedSlots = new HashSet<int>();
            lanes[dot] = commit.Parents.Count > 0 ? commit.Parents[0] : null;
            if (lanes[dot] is not null)
            {
                bottom.Add(new GitGraphSegment(dot, dot));
            }

            for (var p = 1; p < commit.Parents.Count; p++)
            {
                var parent = commit.Parents[p];
                var existing = -1;
                for (var i = 0; i < lanes.Count; i++)
                {
                    if (i != dot && lanes[i] == parent)
                    {
                        existing = i;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    bottom.Add(new GitGraphSegment(dot, existing));
                }
                else
                {
                    // Merge parents branch out to the right of the dot, never across it.
                    var slot = -1;
                    for (var i = dot + 1; i < lanes.Count; i++)
                    {
                        if (lanes[i] is null)
                        {
                            slot = i;
                            break;
                        }
                    }

                    if (slot < 0)
                    {
                        lanes.Add(parent);
                        slot = lanes.Count - 1;
                    }
                    else
                    {
                        lanes[slot] = parent;
                    }

                    reusedSlots.Add(slot);
                    bottom.Add(new GitGraphSegment(dot, slot));
                }
            }

            // Unrelated lanes pass straight through the bottom half. Slots that were freed
            // and re-assigned in this very row must not draw the old lane's rail (artifact).
            for (var i = 0; i < lanes.Count; i++)
            {
                if (i != dot
                    && lanes[i] is not null
                    && i < wasActive.Length
                    && wasActive[i]
                    && !incoming.Contains(i)
                    && !reusedSlots.Contains(i))
                {
                    bottom.Add(new GitGraphSegment(i, i));
                }
            }

            maxLane = Math.Max(maxLane, Math.Max(dot,
                top.Concat(bottom).Select(s => Math.Max(s.From, s.To)).DefaultIfEmpty(0).Max()));

            rows.Add(new GitGraphRow
            {
                Sha = commit.Sha,
                ShortSha = commit.Sha.Length > 7 ? commit.Sha[..7] : commit.Sha,
                Message = commit.Message,
                Author = commit.Author,
                When = commit.When,
                Lane = dot,
                TopSegments = top,
                BottomSegments = bottom,
                Refs = commit.Refs,
            });

            while (lanes.Count > 0 && lanes[^1] is null)
            {
                lanes.RemoveAt(lanes.Count - 1);
            }
        }

        return new GitGraph { Rows = rows, MaxLane = maxLane };
    }
}

using System.Text.RegularExpressions;

namespace StoryEditor2.Parsing;

public sealed record GoalTreeNode(GoalSymbol Goal, string? ChildMode, IReadOnlyList<GoalTreeNode> Children);

public sealed record GoalHierarchyResult(
    IReadOnlyList<GoalTreeNode> Roots,
    IReadOnlyList<string> Diagnostics,
    int LinkCount);

public static class GoalHierarchy
{
    private static readonly Regex ModePattern = new(
        @"Goal\s*\(\s*(\d+)\s*\)\s*\.\s*SubGoals\s*\(\s*(AND|OR)\s*\)\s*;",
        RegexOptions.Compiled);
    private static readonly Regex ChildPattern = new(
        @"Goal\s*\(\s*(\d+)\s*\)\s*\.\s*SubGoal\s*\(\s*(\d+)\s*\)\s*;",
        RegexOptions.Compiled);

    public static GoalHierarchyResult Build(string source, GoalCatalog catalog)
    {
        var modes = new Dictionary<int, string>();
        var children = new Dictionary<int, List<int>>();
        var parents = new Dictionary<int, HashSet<int>>();
        var diagnostics = new List<string>();

        foreach (Match m in ModePattern.Matches(source))
        {
            int parent = int.Parse(m.Groups[1].Value);
            string mode = m.Groups[2].Value;
            if (modes.TryGetValue(parent, out var old) && old != mode)
                diagnostics.Add($"Goal {parent}: conflicting SubGoals modes ({old}/{mode}).");
            modes[parent] = mode;
        }

        foreach (Match m in ChildPattern.Matches(source))
        {
            int parent = int.Parse(m.Groups[1].Value);
            int child = int.Parse(m.Groups[2].Value);
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<int>();
            if (list.Contains(child))
            {
                diagnostics.Add($"Goal {parent}: duplicate SubGoal({child}) link.");
                continue;
            }
            list.Add(child);
            if (!parents.TryGetValue(child, out var parentSet)) parents[child] = parentSet = new HashSet<int>();
            parentSet.Add(parent);
            if (!catalog.Goals.Any(g => g.Id == parent))
                diagnostics.Add($"SubGoal declaration refers to missing parent Goal {parent}.");
            if (!catalog.Goals.Any(g => g.Id == child))
                diagnostics.Add($"Goal {parent} refers to missing child Goal {child}.");
        }

        foreach (var pair in parents.Where(p => p.Value.Count > 1))
            diagnostics.Add($"Goal {pair.Key} is referenced by multiple parents: {string.Join(", ", pair.Value.Order())}.");

        var symbols = catalog.Goals.ToDictionary(g => g.Id);
        var roots = catalog.Goals.Where(g => !parents.ContainsKey(g.Id))
            .OrderBy(g => g.Id).Select(g => BuildNode(g.Id, new HashSet<int>())).ToArray();

        int linkCount = children.Values.Sum(x => x.Count);
        foreach (var goal in catalog.Goals)
            if (!roots.Any(r => Contains(r, goal.Id)) && parents.ContainsKey(goal.Id))
            {
                // A node not reachable from any root is usually part of a cycle.
                var reachable = roots.Any(r => Contains(r, goal.Id));
                if (!reachable) diagnostics.Add($"Goal {goal.Id} is not reachable from a root (possible cycle).");
            }

        return new GoalHierarchyResult(roots, diagnostics.Distinct().ToArray(), linkCount);

        GoalTreeNode BuildNode(int id, HashSet<int> path)
        {
            if (!symbols.TryGetValue(id, out var symbol))
                throw new InvalidOperationException($"Missing Goal {id} should not be materialized.");
            if (!path.Add(id))
            {
                diagnostics.Add($"Cycle detected at Goal {id}.");
                return new GoalTreeNode(symbol, modes.GetValueOrDefault(id), Array.Empty<GoalTreeNode>());
            }
            var nested = new List<GoalTreeNode>();
            if (children.TryGetValue(id, out var ids))
                foreach (int child in ids)
                {
                    if (!symbols.ContainsKey(child)) continue;
                    if (path.Contains(child))
                    {
                        diagnostics.Add($"Cycle detected: Goal {id} -> Goal {child}.");
                        continue;
                    }
                    nested.Add(BuildNode(child, new HashSet<int>(path)));
                }
            return new GoalTreeNode(symbol, modes.GetValueOrDefault(id), nested);
        }

        static bool Contains(GoalTreeNode node, int id) =>
            node.Goal.Id == id || node.Children.Any(c => Contains(c, id));
    }
}

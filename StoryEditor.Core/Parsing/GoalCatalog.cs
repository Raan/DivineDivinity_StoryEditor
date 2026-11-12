using System.Text.RegularExpressions;

namespace StoryEditor2.Parsing;

public sealed record GoalSymbol(int Id, string? Title, SyntaxBlock Block);

public sealed class GoalCatalog
{
    private static readonly Regex GoalBlockId = new(
        @"^Goal\s*\(\s*(\d+)\s*\)$", RegexOptions.Compiled);
    private static readonly Regex GoalTitle = new(
        @"^Goal\s*\(\s*(\d+)\s*\)\s*\.\s*Title\s*\(\s*""((?:\\.|[^""\\])*)""\s*\)\s*;$",
        RegexOptions.Compiled);

    private readonly Dictionary<int, GoalSymbol> _byId;

    private GoalCatalog(Dictionary<int, GoalSymbol> byId) => _byId = byId;

    public IReadOnlyCollection<GoalSymbol> Goals => _byId.Values.OrderBy(g => g.Id).ToArray();

    public static GoalCatalog Build(IReadOnlyList<SyntaxBlock> blocks)
    {
        var titles = new Dictionary<int, string>();
        var goalBlocks = new Dictionary<int, SyntaxBlock>();

        foreach (var block in Flatten(blocks))
        {
            var titleMatch = GoalTitle.Match(block.Header.Trim());
            if (block.Kind == SyntaxKind.GoalTitle && titleMatch.Success &&
                int.TryParse(titleMatch.Groups[1].Value, out int titleId))
                titles[titleId] = titleMatch.Groups[2].Value;

            var blockMatch = GoalBlockId.Match(block.Header.Trim());
            if (block.Kind == SyntaxKind.GoalBlock && blockMatch.Success &&
                int.TryParse(blockMatch.Groups[1].Value, out int blockId))
                goalBlocks[blockId] = block;
        }

        var result = new Dictionary<int, GoalSymbol>();
        foreach (var pair in goalBlocks)
            result[pair.Key] = new GoalSymbol(pair.Key,
                titles.TryGetValue(pair.Key, out var title) ? title : null, pair.Value);
        return new GoalCatalog(result);
    }

    public string GetDisplayName(SyntaxBlock block)
    {
        var match = GoalBlockId.Match(block.Header.Trim());
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int id))
            return block.Header;
        if (!_byId.TryGetValue(id, out var goal))
            return $"Goal {id}";
        return goal.Title is null ? $"Goal {id}" : $"Goal {id} — {goal.Title}";
    }

    private static IEnumerable<SyntaxBlock> Flatten(IEnumerable<SyntaxBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            foreach (var child in Flatten(block.Children)) yield return child;
        }
    }
}

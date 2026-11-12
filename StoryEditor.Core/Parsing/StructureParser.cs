using System.Text.RegularExpressions;
using StoryEditor2.Lexing;

namespace StoryEditor2.Parsing;

public sealed class StructureParser
{
    private sealed class Builder
    {
        public required SyntaxKind Kind { get; init; }
        public required string Header { get; init; }
        public required int Start { get; init; }
        public List<SyntaxBlock> Children { get; } = new();
    }

    private static readonly Regex GoalTitlePattern = new(
        @"(?m)^[ \t]*Goal\s*\(\s*\d+\s*\)\s*\.\s*Title\s*\(\s*""(?:\\.|[^""\\])*""\s*\)\s*;",
        RegexOptions.Compiled);

    public StructureParseResult Parse(string source, IReadOnlyList<Token> tokens)
    {
        var roots = new List<SyntaxBlock>();
        var stack = new Stack<Builder>();
        var diagnostics = new List<ParseDiagnostic>();
        var significant = tokens.Where(t => t.Kind != TokenKind.Trivia &&
                                             t.Kind != TokenKind.EndOfFile).ToArray();
        int segmentStart = 0;

        foreach (var token in significant)
        {
            if (token.Text == "{")
            {
                int start = segmentStart < token.Start ? segmentStart : token.Start;
                string header = source[start..token.Start].Trim();
                if (header.Length == 0) header = "(anonymous block)";
                stack.Push(new Builder { Kind = Classify(header), Header = header, Start = start });
                segmentStart = token.End;
            }
            else if (token.Text == "}")
            {
                if (stack.Count == 0)
                    diagnostics.Add(new ParseDiagnostic("Closing brace without matching opening brace.", token.Start));
                else
                {
                    var builder = stack.Pop();
                    var block = new SyntaxBlock(builder.Kind, builder.Header, builder.Start,
                        token.End - builder.Start, builder.Children.ToArray());
                    if (stack.Count > 0) stack.Peek().Children.Add(block);
                    else roots.Add(block);
                }
                segmentStart = token.End;
            }
            else if (token.Text == ";")
            {
                segmentStart = token.End;
            }
        }

        foreach (Match match in GoalTitlePattern.Matches(source))
        {
            roots.Add(new SyntaxBlock(SyntaxKind.GoalTitle, match.Value.Trim(),
                match.Index, match.Length, Array.Empty<SyntaxBlock>()));
        }

        foreach (var unclosed in stack)
            diagnostics.Add(new ParseDiagnostic($"Unclosed block: {unclosed.Header}", source.Length));

        roots.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new StructureParseResult(roots, diagnostics);
    }

    private static SyntaxKind Classify(string header)
    {
        string value = header.TrimStart();
        if (value.StartsWith("type", StringComparison.Ordinal)) return SyntaxKind.TypeDeclaration;
        if (value.StartsWith("object", StringComparison.Ordinal)) return SyntaxKind.ObjectDeclaration;
        if (value.StartsWith("INIT", StringComparison.Ordinal)) return SyntaxKind.InitSection;
        if (value.StartsWith("EXIT", StringComparison.Ordinal)) return SyntaxKind.ExitSection;
        if (value.StartsWith("KB", StringComparison.Ordinal)) return SyntaxKind.KnowledgeBaseSection;
        if (value.StartsWith("Goal", StringComparison.Ordinal)) return SyntaxKind.GoalBlock;
        return SyntaxKind.Unknown;
    }
}

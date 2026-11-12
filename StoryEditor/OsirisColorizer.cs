using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using StoryEditor2.Lexing;
using StoryEditor2.Parsing;

namespace StoryEditor2.Wpf;

public sealed class OsirisColorizer : DocumentColorizingTransformer
{
    // High-contrast palette designed for the dark editor background.
    private static readonly Brush DefaultBrush = Freeze("#FFD4D4D4");
    private static readonly Brush KeywordBrush = Freeze("#FF569CD6");
    private static readonly Brush StringBrush = Freeze("#FFCE9178");
    private static readonly Brush NumberBrush = Freeze("#FFB5CEA8");
    private static readonly Brush VariableBrush = Freeze("#FFDCDCAA");
    private static readonly Brush ObjectBrush = Freeze("#FF4EC9B0");
    private static readonly Brush TypeBrush = Freeze("#FF4FC1FF");
    private static readonly Brush BuiltinBrush = Freeze("#FFDCDCAA");
    private static readonly Brush EventBrush = Freeze("#FFFFA657");
    private static readonly Brush QueryBrush = Freeze("#FF4FC1FF");
    private static readonly Brush CallBrush = Freeze("#FF6A9955");
    private static readonly Brush SystemBrush = Freeze("#FFC586C0");
    private static readonly Brush OsirisCallBrush = Freeze("#FFD7BA7D");
    private static readonly Brush CommentBrush = Freeze("#FF6A9955");
    private static readonly Brush OperatorBrush = Freeze("#FFD4D4D4");

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "IF", "THEN", "PROC", "AND", "OR", "NOT", "IFF", "INTEGER", "REAL",
        "STRING", "Goal", "INIT", "EXIT", "KB", "SubGoal", "SubGoals",
        "Title", "object", "type", "call", "query", "event", "syscall",
        "sysquery", "[in]", "[out]"
    };

    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "GoalCompleted", "SetStatus", "MergeStatus"
    };

    private IReadOnlyList<Token> _tokens = Array.Empty<Token>();
    private ApiSymbolCatalog? _symbols;

    public void Update(string text, ApiSymbolCatalog? symbols)
    {
        _tokens = Lexer.Tokenize(text);
        _symbols = symbols;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        int start = line.Offset;
        int end = line.EndOffset;
        int index = LowerBound(start);

        while (index < _tokens.Count)
        {
            var token = _tokens[index++];
            if (token.Start >= end) break;
            if (token.Length == 0 || token.End <= start) continue;

            Brush? brush = GetBrush(token, index - 1);
            if (brush is null) continue;

            int tokenStart = Math.Max(start, token.Start);
            int tokenEnd = Math.Min(end, token.End);
            if (tokenEnd <= tokenStart) continue;

            ChangeLinePart(tokenStart, tokenEnd, element =>
                element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private Brush? GetBrush(Token token, int tokenIndex)
    {
        if (token.Kind == TokenKind.StringLiteral) return StringBrush;
        if (token.Kind == TokenKind.IntegerLiteral || token.Kind == TokenKind.RealLiteral) return NumberBrush;
        if (token.Kind == TokenKind.Trivia)
        {
            string trimmed = token.Text.TrimStart();
            return trimmed.StartsWith("//", StringComparison.Ordinal) ||
                   trimmed.StartsWith("/*", StringComparison.Ordinal) ? CommentBrush : null;
        }
        if (token.Kind != TokenKind.Identifier)
            return token.Text is "==" or "!=" or "<" or ">" or "<=" or ">=" ? OperatorBrush : null;

        if (token.Text.StartsWith("_", StringComparison.Ordinal)) return VariableBrush;
        if (Keywords.Contains(token.Text)) return KeywordBrush;
        if (Builtins.Contains(token.Text)) return BuiltinBrush;
        if (_symbols?.Types.Contains(token.Text) == true) return TypeBrush;
        if (_symbols?.Objects.Contains(token.Text) == true) return ObjectBrush;
        if (_symbols?.Functions.TryGetValue(token.Text, out var kind) == true)
        {
            return kind switch
            {
                ApiSymbolKind.Event => EventBrush,
                ApiSymbolKind.Query => QueryBrush,
                ApiSymbolKind.Call => CallBrush,
                ApiSymbolKind.SystemQuery or ApiSymbolKind.SystemCall => SystemBrush,
                _ => DefaultBrush
            };
        }

        for (int i = tokenIndex + 1; i < _tokens.Count; i++)
        {
            if (_tokens[i].Kind == TokenKind.Trivia) continue;
            return _tokens[i].Text == "(" ? OsirisCallBrush : null;
        }
        return null;
    }

    private int LowerBound(int offset)
    {
        int low = 0, high = _tokens.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (_tokens[mid].End <= offset) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private static Brush Freeze(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
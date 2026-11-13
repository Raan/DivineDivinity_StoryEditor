using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using StoryEditor2.Lexing;

namespace StoryEditor2.Wpf;

/// <summary>
/// Highlights the matching delimiter next to the caret. Delimiters inside
/// strings and comments are ignored because matching is based on lexer tokens.
/// </summary>
public sealed class BracketPairColorizer : DocumentColorizingTransformer
{
    private static readonly Brush HighlightBrush = CreateHighlightBrush();
    private IReadOnlyList<(int Start, int End)> _matches = Array.Empty<(int, int)>();

    public void Update(string text, int caretOffset)
    {
        var tokens = Lexer.Tokenize(text);
        var pairs = new Dictionary<int, int>();
        var stack = new Stack<int>();

        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Kind is TokenKind.Trivia or TokenKind.StringLiteral or TokenKind.EndOfFile)
                continue;

            string value = token.Text;
            if (IsOpening(value))
            {
                stack.Push(i);
                continue;
            }

            if (!IsClosing(value))
                continue;

            if (stack.Count == 0)
                continue;

            int openingIndex = stack.Peek();
            if (IsPair(tokens[openingIndex].Text, value))
            {
                stack.Pop();
                pairs[openingIndex] = i;
                pairs[i] = openingIndex;
            }
            else
            {
                // A mismatched closer breaks the current nesting chain.
                stack.Clear();
            }
        }

        int candidate = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Kind == TokenKind.Punctuation &&
                IsDelimiter(token.Text) &&
                (token.Start == caretOffset || token.End == caretOffset))
            {
                candidate = i;
                if (token.Start == caretOffset)
                    break;
            }
        }

        if (candidate >= 0 && pairs.TryGetValue(candidate, out int partner))
        {
            Token first = tokens[candidate];
            Token second = tokens[partner];
            _matches = new[]
            {
                (first.Start, first.End),
                (second.Start, second.End)
            };
        }
        else
        {
            _matches = Array.Empty<(int, int)>();
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var match in _matches)
        {
            int start = Math.Max(line.Offset, match.Start);
            int end = Math.Min(line.EndOffset, match.End);
            if (end <= start)
                continue;

            ChangeLinePart(start, end, element =>
                element.TextRunProperties.SetBackgroundBrush(HighlightBrush));
        }
    }

    // Parentheses are the regular grouping delimiters in Osiris calls.
    // Curly braces are intentionally not treated as language syntax here.
    private static bool IsOpening(string value) => value == "(";
    private static bool IsClosing(string value) => value == ")";
    private static bool IsDelimiter(string value) => IsOpening(value) || IsClosing(value);

    private static bool IsPair(string opening, string closing) =>
        opening == "(" && closing == ")";

    private static Brush CreateHighlightBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(95, 86, 156, 214));
        brush.Freeze();
        return brush;
    }
}

using StoryEditor2.Lexing;

namespace StoryEditor2.Parsing;

public enum RuleKind { If, Proc }

public enum RuleConditionConnector { First, And, AndNot }

/// <summary>
/// Keeps the editor's NOT state and source-level AND/AND NOT connector in sync.
/// The first condition uses a leading NOT; subsequent conditions encode negation
/// in their connector.
/// </summary>
public static class RuleConditionSemantics
{
    public static RuleConditionConnector ConnectorFor(bool negated, int index) =>
        index == 0 ? RuleConditionConnector.First :
        negated ? RuleConditionConnector.AndNot : RuleConditionConnector.And;

    public static bool IsNegated(RuleConditionConnector connector, bool leadingNegated, int index) =>
        index == 0 ? leadingNegated : connector == RuleConditionConnector.AndNot;
}

/// <summary>
/// Captures the exact source text between parsed elements while replacing
/// structural connectors and separators with movable placeholders.
/// </summary>
public static class RuleTriviaPreserver
{
    public const string ConnectorMarker = "__STORYEDITOR_CONNECTOR_MARKER__";
    public const string SeparatorMarker = "__STORYEDITOR_SEPARATOR_MARKER__";

    public static IReadOnlyList<string> ExtractConditionLayouts(
        string source, IEnumerable<(int Start, int Length)> elementSpans)
    {
        var spans = elementSpans.OrderBy(span => span.Start).ToArray();
        var layouts = Enumerable.Repeat(string.Empty, spans.Length).ToArray();
        for (int i = 1; i < spans.Length; i++)
        {
            int gapStart = spans[i - 1].Start + spans[i - 1].Length;
            int gapEnd = spans[i].Start;
            if (gapStart < 0 || gapEnd < gapStart || gapEnd > source.Length)
                throw new ArgumentOutOfRangeException(nameof(elementSpans));

            string gap = source[gapStart..gapEnd];
            var tokens = Lexer.Tokenize(gap)
                .Where(token => token.Kind != TokenKind.Trivia &&
                    token.Kind != TokenKind.EndOfFile).ToArray();
            var replacements = new List<(int Start, int Length, string Value)>();
            for (int t = 0; t < tokens.Length; t++)
            {
                if (tokens[t].Text != "AND") continue;
                if (t + 1 < tokens.Length && tokens[t + 1].Text == "NOT")
                {
                    int end = tokens[t + 1].Start + tokens[t + 1].Length;
                    replacements.Add((tokens[t].Start, end - tokens[t].Start, ConnectorMarker));
                    t++;
                }
                else
                    replacements.Add((tokens[t].Start, tokens[t].Length, ConnectorMarker));
                break;
            }
            foreach (var replacement in replacements.OrderByDescending(item => item.Start))
                gap = gap.Remove(replacement.Start, replacement.Length)
                    .Insert(replacement.Start, replacement.Value);
            layouts[i] = gap;
        }
        return layouts;
    }

    public static IReadOnlyList<string> ExtractActionLayouts(
        string source, IEnumerable<(int Start, int Length)> elementSpans)
    {
        var spans = elementSpans.OrderBy(span => span.Start).ToArray();
        var layouts = Enumerable.Repeat(string.Empty, spans.Length).ToArray();
        for (int i = 1; i < spans.Length; i++)
        {
            int gapStart = spans[i - 1].Start + spans[i - 1].Length;
            int gapEnd = spans[i].Start;
            if (gapStart < 0 || gapEnd < gapStart || gapEnd > source.Length)
                throw new ArgumentOutOfRangeException(nameof(elementSpans));

            string gap = source[gapStart..gapEnd];
            var semicolon = Lexer.Tokenize(gap).FirstOrDefault(token => token.Text == ";");
            if (semicolon is not null)
                gap = gap.Remove(semicolon.Start, semicolon.Length)
                    .Insert(semicolon.Start, SeparatorMarker);
            layouts[i] = gap;
        }
        return layouts;
    }
}

public sealed record RuleCondition(string Text, int Start, int Length, bool Negated, RuleConditionConnector Connector = RuleConditionConnector.First);
public sealed record RuleAction(string Text, int Start, int Length, bool Negated);
public sealed record CallArgument(string Text, int Start, int Length);
public sealed record FunctionCallExpression(
    string Name, int Start, int Length, IReadOnlyList<CallArgument> Arguments);

public sealed record OsirisRule(
    RuleKind Kind, int Start, int Length,
    IReadOnlyList<RuleCondition> Conditions,
    IReadOnlyList<RuleAction> Actions,
    IReadOnlyList<FunctionCallExpression> Calls,
    string? ProcedureSignature = null,
    int ProcedureSignatureStart = -1,
    int ProcedureSignatureLength = 0);

public sealed record RuleParseResult(
    IReadOnlyList<OsirisRule> Rules,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    int FunctionCallCount);

/// <summary>
/// Conservative parser for the confirmed IF/PROC -> conditions -> THEN -> actions form.
/// Source spans are retained. This is an AST-building first pass, not full grammar validation.
/// </summary>
public sealed class RuleParser
{
    public RuleParseResult Parse(string source, IReadOnlyList<Token> allTokens,
        IEnumerable<SyntaxBlock> blocks)
    {
        var tokens = allTokens.Where(t => t.Kind != TokenKind.Trivia &&
            t.Kind != TokenKind.EndOfFile).ToArray();
        var rules = new List<OsirisRule>();
        var diagnostics = new List<ParseDiagnostic>();

        foreach (var kb in Flatten(blocks).Where(b => b.Kind == SyntaxKind.KnowledgeBaseSection))
        {
            int first = Array.FindIndex(tokens, t => t.Start >= kb.Start && t.Text == "{");
            int last = Array.FindLastIndex(tokens, t => t.Start < kb.End && t.Text == "}");
            if (first < 0 || last <= first) continue;

            int i = first + 1;
            while (i < last)
            {
                if (tokens[i].Text is not ("IF" or "PROC")) { i++; continue; }

                int ruleStartIndex = i;
                var kind = tokens[i].Text == "IF" ? RuleKind.If : RuleKind.Proc;
                int then = FindAtDepth(tokens, i + 1, last, "THEN");
                if (then < 0)
                {
                    diagnostics.Add(new ParseDiagnostic("Rule is missing THEN.", tokens[i].Start));
                    break;
                }

                int conditionStart = i + 1;
                string? procedureSignature = null;
                int procedureSignatureStart = -1;
                int procedureSignatureLength = 0;

                // A PROC header is its own signature, not a logical condition.
                // Example: PROC UpdateTime((INTEGER)_Var1, (INTEGER)_Var2)
                if (kind == RuleKind.Proc && conditionStart + 2 < then &&
                    tokens[conditionStart].Kind == TokenKind.Identifier &&
                    tokens[conditionStart + 1].Text == "(")
                {
                    int signatureClose = FindMatchingParen(tokens, conditionStart + 1, then);
                    if (signatureClose >= 0)
                    {
                        procedureSignatureStart = tokens[conditionStart].Start;
                        procedureSignatureLength = tokens[signatureClose].End - procedureSignatureStart;
                        procedureSignature = source.Substring(procedureSignatureStart, procedureSignatureLength);
                        conditionStart = signatureClose + 1;
                    }
                    else
                    {
                        diagnostics.Add(new ParseDiagnostic("PROC signature has an unmatched opening parenthesis.", tokens[conditionStart + 1].Start));
                    }
                }

                var conditions = SplitConditions(source, tokens, conditionStart, then, diagnostics,
                    kind == RuleKind.Proc);
                int actionStart = then + 1;
                int nextRule = FindNextRule(tokens, actionStart, last);
                int actionEnd = nextRule < 0 ? last : nextRule;
                var actions = SplitActions(source, tokens, actionStart, actionEnd, diagnostics);
                var calls = ParseCalls(source, tokens, i + 1, actionEnd);
                int endPosition = actionEnd > actionStart
                    ? tokens[actionEnd - 1].End : tokens[then].End;
                rules.Add(new OsirisRule(kind, tokens[ruleStartIndex].Start,
                    Math.Max(0, endPosition - tokens[ruleStartIndex].Start),
                    conditions, actions, calls, procedureSignature, procedureSignatureStart, procedureSignatureLength));

                i = nextRule >= 0 ? nextRule : last;
            }
        }

        return new RuleParseResult(rules, diagnostics, rules.Sum(r => r.Calls.Count));
    }

    private static IReadOnlyList<RuleCondition> SplitConditions(string source, Token[] t,
        int start, int end, List<ParseDiagnostic> diagnostics, bool procRule)
    {
        var result = new List<RuleCondition>();
        int segment = start, depth = 0;
        var connector = RuleConditionConnector.First;
        bool negated = false;

        // PROC signatures may be followed directly by THEN when the procedure
        // intentionally has no conditions. IF rules are checked by the grammar validator.
        if (start >= end)
            return result;

        if (procRule && segment < end &&
            string.Equals(t[segment].Text, "AND", StringComparison.OrdinalIgnoreCase))
        {
            segment++;
            if (segment < end &&
                string.Equals(t[segment].Text, "NOT", StringComparison.OrdinalIgnoreCase))
            {
                negated = true;
                segment++;
            }
        }
        else if (segment < end && t[segment].Text == "NOT")
        {
            negated = true;
            segment++;
        }

        for (int i = segment; i <= end; i++)
        {
            string value = i < end ? t[i].Text : "";
            if (value == "(") depth++;
            else if (value == ")") depth--;

            // A missing parenthesis in the current condition must not swallow
            // every following condition. Osiris commonly places logical
            // connectors at the beginning of a new line, so accept AND there
            // as a recovery point even when the previous expression is unbalanced.
            // The lexer has already removed comments/trivia from this token stream,
            // therefore connector-like text inside comments is never considered.
            bool isAnd = string.Equals(value, "AND", StringComparison.OrdinalIgnoreCase);
            bool lineStartRecovery = depth != 0 && isAnd &&
                IsFirstTokenOnLine(source, t[i].Start);

            if (isAnd && (depth == 0 || lineStartRecovery))
            {
                AddCondition(segment, i, negated, connector);
                bool nextNegated = i + 1 < end &&
                    string.Equals(t[i + 1].Text, "NOT", StringComparison.OrdinalIgnoreCase);
                connector = nextNegated
                    ? RuleConditionConnector.AndNot
                    : RuleConditionConnector.And;
                negated = nextNegated;
                segment = nextNegated ? i + 2 : i + 1;
                if (nextNegated) i++;
            }
            else if (i == end)
            {
                AddCondition(segment, i, negated, connector);
            }
        }

        if (result.Count == 0 && start < end)
            diagnostics.Add(new ParseDiagnostic("Could not identify condition atoms.", t[start].Start));
        return result;

        void AddCondition(int a, int b, bool isNegated, RuleConditionConnector conditionConnector)
        {
            // Keep every token in the condition span, including grouping parentheses.
            if (a >= b)
            {
                int position = a < t.Length ? t[a].Start : source.Length;
                diagnostics.Add(new ParseDiagnostic("Empty condition around AND connector.", position));
                return;
            }
            int s = t[a].Start, e = t[b - 1].End;
            result.Add(new RuleCondition(source[s..e], s, e - s, isNegated, conditionConnector));
        }
    }
    private static bool IsFirstTokenOnLine(string source, int position)
    {
        if (position <= 0 || position > source.Length)
            return true;

        int lineStart = position - 1;
        while (lineStart >= 0 && source[lineStart] is not ('\r' or '\n'))
            lineStart--;

        for (int i = lineStart + 1; i < position; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<RuleAction> SplitActions(string source, Token[] t,
        int start, int end, List<ParseDiagnostic> diagnostics)
    {
        var result = new List<RuleAction>();
        int segment = start;
        bool negated = false;
        for (int i = start; i < end; i++)
        {
            if (t[i].Text == "NOT" && i == segment) { negated = true; segment++; continue; }
            if (t[i].Text == ";")
            {
                if (segment < i)
                {
                    int s = t[segment].Start, e = t[i - 1].End;
                    result.Add(new RuleAction(source[s..e], s, e - s, negated));
                }
                else diagnostics.Add(new ParseDiagnostic("Empty action before semicolon.", t[i].Start));
                segment = i + 1; negated = false;
            }
        }
        if (segment < end)
            diagnostics.Add(new ParseDiagnostic("Action is missing terminating semicolon.", t[segment].Start));
        return result;
    }

    private static IReadOnlyList<FunctionCallExpression> ParseCalls(
        string source, Token[] t, int start, int end)
    {
        var result = new List<FunctionCallExpression>();
        for (int i = start; i + 1 < end; i++)
        {
            if (t[i].Kind != TokenKind.Identifier || t[i + 1].Text != "(") continue;
            int close = FindMatchingParen(t, i + 1, end);
            if (close < 0) continue;
            var args = new List<CallArgument>();
            int argStart = i + 2, depth = 0;
            for (int j = argStart; j <= close; j++)
            {
                string v = j < close ? t[j].Text : ")";
                if (v == "(") depth++;
                else if (v == ")") depth--;
                if (j == close || (v == "," && depth == 0))
                {
                    if (argStart < j)
                    {
                        int s = t[argStart].Start, e = t[j - 1].End;
                        args.Add(new CallArgument(source[s..e], s, e - s));
                    }
                    argStart = j + 1;
                }
            }
            int callStart = t[i].Start, callEnd = t[close].End;
            result.Add(new FunctionCallExpression(t[i].Text, callStart,
                callEnd - callStart, args));
        }
        return result;
    }

    private static int FindMatchingParen(Token[] t, int open, int end)
    {
        int depth = 0;
        for (int i = open; i < end; i++)
        {
            if (t[i].Text == "(") depth++;
            else if (t[i].Text == ")" && --depth == 0) return i;
        }
        return -1;
    }

    private static int FindAtDepth(Token[] t, int start, int end, string sought)
    {
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            // THEN is a synchronization point: even if a condition contains an
            // unmatched opening parenthesis, resume rule parsing at THEN rather
            // than consuming the rest of the KB as part of that condition.
            if (t[i].Text == sought && (depth == 0 || sought == "THEN"))
                return i;

            if (t[i].Text == "(") depth++;
            else if (t[i].Text == ")") depth--;
        }
        return -1;
    }

    private static int FindNextRule(Token[] t, int start, int end)
    {
        for (int i = start; i < end; i++)
            if (t[i].Text is "IF" or "PROC") return i;
        return -1;
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

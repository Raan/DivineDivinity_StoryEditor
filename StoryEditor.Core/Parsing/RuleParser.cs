using StoryEditor2.Lexing;

namespace StoryEditor2.Parsing;

public enum RuleKind { If, Proc }

public enum RuleConditionConnector { First, And, AndNot }

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

                var conditions = SplitConditions(source, tokens, conditionStart, then, diagnostics);
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
        int start, int end, List<ParseDiagnostic> diagnostics)
    {
        var result = new List<RuleCondition>();
        int segment = start, depth = 0;
        var connector = RuleConditionConnector.First;
        bool negated = false;

        if (segment < end && t[segment].Text == "NOT")
        {
            negated = true;
            segment++;
        }

        for (int i = segment; i <= end; i++)
        {
            string value = i < end ? t[i].Text : "";
            if (value == "(") depth++;
            else if (value == ")") depth--;

            if (depth == 0 && value == "AND")
            {
                AddCondition(segment, i, negated, connector);
                bool nextNegated = i + 1 < end && t[i + 1].Text == "NOT";
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
            if (a >= b) return;
            int s = t[a].Start, e = t[b - 1].End;
            result.Add(new RuleCondition(source[s..e], s, e - s, isNegated, conditionConnector));
        }
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
            if (t[i].Text == "(") depth++;
            else if (t[i].Text == ")") depth--;
            else if (depth == 0 && t[i].Text == sought) return i;
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

using StoryEditor2.Lexing;

namespace StoryEditor2.Parsing;

public enum GrammarDiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed record GrammarDiagnostic(
    string Code,
    GrammarDiagnosticSeverity Severity,
    string Message,
    int Position,
    int Length = 1);

public sealed record GrammarValidationResult(
    IReadOnlyList<GrammarDiagnostic> Diagnostics,
    int RuleCount,
    int ConditionCount,
    int ActionCount,
    int LifecycleStatementCount)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity == GrammarDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == GrammarDiagnosticSeverity.Warning);
}

/// <summary>
/// Validates the confirmed source-level Osiris constructs used by Divine Divinity.
/// Unknown Story-defined predicates remain legal; API checks are applied only when
/// a name has a declaration in the source catalog.
/// </summary>
public sealed class OsirisGrammarValidator
{
    private static readonly HashSet<string> ComparisonOperators = new(StringComparer.Ordinal)
    {
        "=", "==", "!=", "<", ">", "<=", ">="
    };

    public GrammarValidationResult Validate(string source, ApiSymbolCatalog? api = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<GrammarDiagnostic>();
        var tokens = Lexer.Tokenize(source);

        ValidateLexical(source, diagnostics);
        ValidateDelimiters(tokens, diagnostics);

        var structure = new StructureParser().Parse(source, tokens);
        foreach (var diagnostic in structure.Diagnostics)
            Add(diagnostics, "STR001", diagnostic.Message, diagnostic.Position);

        api ??= ApiSymbolCatalog.Build(source);

        var ruleResult = new RuleParser().Parse(source, tokens, structure.Blocks);
        foreach (var diagnostic in ruleResult.Diagnostics)
            Add(diagnostics, "KB001", diagnostic.Message, diagnostic.Position);

        int conditionCount = 0;
        int actionCount = 0;
        foreach (var rule in ruleResult.Rules)
        {
            if (rule.Kind == RuleKind.If && rule.Conditions.Count == 0)
                Add(diagnostics, "KB002", "IF rule must contain at least one condition before THEN.", rule.Start);

            if (rule.Actions.Count == 0)
                Add(diagnostics, "KB003", "Rule must contain at least one action after THEN.", rule.Start);

            conditionCount += rule.Conditions.Count;
            actionCount += rule.Actions.Count;

            foreach (var condition in rule.Conditions)
                ValidateCondition(condition, api, diagnostics);

            foreach (var action in rule.Actions)
                ValidateAction(action, api, diagnostics);

            if (rule.Kind == RuleKind.Proc)
                ValidateProcedureSignature(rule, diagnostics);
        }

        ValidateKnowledgeBaseCoverage(tokens, structure.Blocks, ruleResult.Rules, diagnostics);

        int lifecycleStatementCount = 0;
        foreach (var section in Flatten(structure.Blocks)
                     .Where(block => block.Kind is SyntaxKind.InitSection or SyntaxKind.ExitSection))
        {
            if (section.Start < 0 || section.End > source.Length || section.Length <= 0)
                continue;

            string sectionText = source.Substring(section.Start, section.Length);
            var parsed = new ActionListParser().Parse(sectionText, api);
            lifecycleStatementCount += parsed.Statements.Count;
            foreach (var diagnostic in parsed.Diagnostics)
                Add(diagnostics, "LIFE001", diagnostic.Message, section.Start + diagnostic.Position);
        }

        var ordered = diagnostics
            .GroupBy(d => (d.Code, d.Position, d.Message))
            .Select(group => group.First())
            .OrderBy(d => d.Position)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ToArray();

        return new GrammarValidationResult(
            ordered, ruleResult.Rules.Count, conditionCount, actionCount, lifecycleStatementCount);
    }

    private static void ValidateCondition(
        RuleCondition condition,
        ApiSymbolCatalog api,
        List<GrammarDiagnostic> diagnostics)
    {
        var tokens = Significant(Lexer.Tokenize(condition.Text));
        if (tokens.Length == 0)
        {
            Add(diagnostics, "KB010", "Condition cannot be empty.", condition.Start);
            return;
        }

        int comparisonIndex = -1;
        int depth = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Text == "(") depth++;
            else if (tokens[i].Text == ")") depth--;
            else if (depth == 0 && ComparisonOperators.Contains(tokens[i].Text))
            {
                if (comparisonIndex >= 0)
                {
                    Add(diagnostics, "KB011", "A condition may contain only one top-level comparison operator.", condition.Start + tokens[i].Start);
                    return;
                }
                comparisonIndex = i;
            }
        }

        if (comparisonIndex >= 0)
        {
            if (!IsValue(tokens, 0, comparisonIndex) ||
                !IsValue(tokens, comparisonIndex + 1, tokens.Length))
            {
                Add(diagnostics, "KB012", "Comparison must have a valid value on both sides.", condition.Start);
            }
            return;
        }

        if (!TryParseCall(tokens, out string? name, out int errorIndex))
        {
            Add(diagnostics, "KB013", "Expected a predicate call in the form Name(arguments) or a comparison such as _Var1 != 0.",
                condition.Start + (errorIndex >= 0 && errorIndex < tokens.Length ? tokens[errorIndex].Start : 0));
            return;
        }

        ValidateApiKind(name!, isAction: false,
            condition.Start + tokens[0].Start, api, diagnostics);
    }

    private static void ValidateAction(
        RuleAction action,
        ApiSymbolCatalog api,
        List<GrammarDiagnostic> diagnostics)
    {
        var tokens = Significant(Lexer.Tokenize(action.Text));
        if (tokens.Length == 0)
        {
            Add(diagnostics, "KB020", "Action cannot be empty.", action.Start);
            return;
        }

        if (tokens.Length == 1 && tokens[0].Text == "GoalCompleted")
            return;

        if (tokens.Length == 2 && tokens[0].Text == "!" &&
            tokens[1].Kind == TokenKind.IntegerLiteral &&
            uint.TryParse(tokens[1].Text, out _))
            return;

        if (!TryParseCall(tokens, out string? name, out int errorIndex))
        {
            Add(diagnostics, "KB021", "Expected an action call in the form Name(arguments);, GoalCompleted; or !GoalId;.",
                action.Start + (errorIndex >= 0 && errorIndex < tokens.Length ? tokens[errorIndex].Start : 0));
            return;
        }

        ValidateApiKind(name!, isAction: true,
            action.Start + tokens[0].Start, api, diagnostics);
    }

    private static void ValidateApiKind(
        string name,
        bool isAction,
        int position,
        ApiSymbolCatalog api,
        List<GrammarDiagnostic> diagnostics)
    {
        if (!api.FunctionSignatures.TryGetValue(name, out var signature))
            return; // User-defined facts and procedures are valid.

        bool validKind = isAction
            ? signature.Kind is ApiSymbolKind.Call or ApiSymbolKind.SystemCall
            : signature.Kind is ApiSymbolKind.Event or ApiSymbolKind.Query or ApiSymbolKind.SystemQuery;

        if (!validKind)
        {
            string expected = isAction ? "action" : "condition";
            Add(diagnostics, "API001", $"'{name}' is declared as {signature.Kind}, not as an Osiris {expected}.", position);
            return;
        }

    }

    private static bool TryParseCall(
        Token[] tokens,
        out string? name,
        out int errorIndex)
    {
        name = null;
        errorIndex = 0;

        if (tokens.Length < 3 || tokens[0].Kind != TokenKind.Identifier || tokens[1].Text != "(")
            return false;

        name = tokens[0].Text;
        int depth = 0;
        int close = -1;
        for (int i = 1; i < tokens.Length; i++)
        {
            if (tokens[i].Text == "(") depth++;
            else if (tokens[i].Text == ")")
            {
                depth--;
                if (depth < 0) { errorIndex = i; return false; }
                if (depth == 0) { close = i; break; }
            }
        }

        if (close != tokens.Length - 1)
        {
            errorIndex = close >= 0 ? close + 1 : tokens.Length - 1;
            return false;
        }

        if (close == 2)
            return true;

        int segmentStart = 2;
        depth = 0;
        for (int i = 2; i < close; i++)
        {
            if (tokens[i].Text == "(") depth++;
            else if (tokens[i].Text == ")") depth--;
            else if (tokens[i].Text == "," && depth == 0)
            {
                if (i == segmentStart || !IsValue(tokens, segmentStart, i))
                {
                    errorIndex = segmentStart;
                    return false;
                }
                segmentStart = i + 1;
            }
        }

        if (segmentStart == close || !IsValue(tokens, segmentStart, close))
        {
            errorIndex = segmentStart < tokens.Length ? segmentStart : close - 1;
            return false;
        }

        return true;
    }

    private static bool IsValue(Token[] tokens, int start, int end)
    {
        if (start >= end) return false;

        if (end - start == 1)
            return tokens[start].Kind is TokenKind.Identifier or TokenKind.IntegerLiteral
                or TokenKind.RealLiteral or TokenKind.StringLiteral;

        if (end - start == 2 && tokens[start].Text is "+" or "-" &&
            tokens[start + 1].Kind is TokenKind.IntegerLiteral or TokenKind.RealLiteral)
            return true;

        // Typed Osiris value: (INTEGER)_Var1
        if (end - start == 4 && tokens[start].Text == "(" &&
            tokens[start + 1].Kind == TokenKind.Identifier &&
            tokens[start + 2].Text == ")" &&
            tokens[start + 3].Kind == TokenKind.Identifier)
            return true;

        // Parenthesized simple value.
        if (end - start == 3 && tokens[start].Text == "(" &&
            tokens[start + 2].Text == ")")
            return IsValue(tokens, start + 1, start + 2);

        return false;
    }

    private static void ValidateProcedureSignature(
        OsirisRule rule,
        List<GrammarDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(rule.ProcedureSignature))
        {
            Add(diagnostics, "PROC001", "PROC rule is missing its procedure signature.", rule.Start);
            return;
        }

        var tokens = Significant(Lexer.Tokenize(rule.ProcedureSignature));
        if (tokens.Length < 3 || tokens[0].Kind != TokenKind.Identifier ||
            tokens[1].Text != "(" || tokens[^1].Text != ")")
        {
            Add(diagnostics, "PROC002", "Invalid PROC signature. Expected Name((TYPE)_Var1, ...).",
                rule.ProcedureSignatureStart);
            return;
        }

        int depth = 0;
        int segmentStart = 2;
        for (int i = 2; i < tokens.Length - 1; i++)
        {
            string value = tokens[i].Text;
            if (value == "(") depth++;
            else if (value == ")") depth--;

            if (depth == 0 && value == ",")
            {
                if (segmentStart >= i)
                    Add(diagnostics, "PROC004", "PROC signature contains an empty parameter.",
                        rule.ProcedureSignatureStart + tokens[i].Start);
                else if (!IsValue(tokens, segmentStart, i))
                    Add(diagnostics, "PROC003", "Invalid Osiris value in PROC parameter list.",
                        rule.ProcedureSignatureStart + tokens[segmentStart].Start);

                segmentStart = i + 1;
            }
        }

        if (segmentStart < tokens.Length - 1 &&
            !IsValue(tokens, segmentStart, tokens.Length - 1))
            Add(diagnostics, "PROC003", "Invalid Osiris value in PROC parameter list.",
                rule.ProcedureSignatureStart + tokens[segmentStart].Start);
    }

    private static void ValidateKnowledgeBaseCoverage(
        IReadOnlyList<Token> allTokens,
        IReadOnlyList<SyntaxBlock> blocks,
        IReadOnlyList<OsirisRule> rules,
        List<GrammarDiagnostic> diagnostics)
    {
        foreach (var kb in Flatten(blocks).Where(block => block.Kind == SyntaxKind.KnowledgeBaseSection))
        {
            int firstToken = LowerBound(allTokens, kb.Start);
            int afterLastToken = LowerBound(allTokens, kb.End);
            int openBrace = -1;
            int closeBrace = -1;
            for (int i = firstToken; i < afterLastToken; i++)
            {
                if (allTokens[i].Text == "{") openBrace = i;
                if (allTokens[i].Text == "}") closeBrace = i;
            }
            if (openBrace < 0 || closeBrace <= openBrace)
                continue;

            int bodyStart = allTokens[openBrace].End;
            int bodyEnd = allTokens[closeBrace].Start;
            var kbRules = rules.Where(rule => rule.Start >= bodyStart && rule.Start < bodyEnd)
                .OrderBy(rule => rule.Start).ToArray();
            int ruleIndex = 0;

            for (int i = openBrace + 1; i < closeBrace; i++)
            {
                var token = allTokens[i];
                if (token.Kind is TokenKind.Trivia or TokenKind.EndOfFile)
                    continue;

                while (ruleIndex < kbRules.Length &&
                    token.Start >= kbRules[ruleIndex].Start + kbRules[ruleIndex].Length)
                    ruleIndex++;

                bool belongsToRule = ruleIndex < kbRules.Length &&
                    token.Start >= kbRules[ruleIndex].Start &&
                    token.Start < kbRules[ruleIndex].Start + kbRules[ruleIndex].Length;
                if (!belongsToRule)
                    Add(diagnostics, "KB030", $"Unexpected token '{token.Text}' outside an IF/PROC rule.", token.Start);
            }
        }
    }

    private static int LowerBound(IReadOnlyList<Token> tokens, int position)
    {
        int low = 0;
        int high = tokens.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (tokens[middle].Start < position) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static void ValidateLexical(string source, List<GrammarDiagnostic> diagnostics)
    {
        int i = 0;
        while (i < source.Length)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] is not '\r' and not '\n') i++;
                continue;
            }

            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                if (i + 1 >= source.Length)
                {
                    Add(diagnostics, "LEX001", "Block comment is not closed.", start, source.Length - start);
                    return;
                }
                i += 2;
                continue;
            }

            if (source[i] == '"')
            {
                int start = i++;
                bool closed = false;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length) { i += 2; continue; }
                    if (source[i] == '"') { i++; closed = true; break; }
                    i++;
                }
                if (!closed)
                    Add(diagnostics, "LEX002", "String literal is not closed.", start, source.Length - start);
                continue;
            }

            i++;
        }
    }

    private static void ValidateDelimiters(
        IReadOnlyList<Token> tokens,
        List<GrammarDiagnostic> diagnostics)
    {
        var stack = new Stack<Token>();
        foreach (var token in tokens)
        {
            if (token.Kind is TokenKind.Trivia or TokenKind.EndOfFile or TokenKind.StringLiteral)
                continue;

            if (token.Text is "(" or "[")
                stack.Push(token);
            else if (token.Text is ")" or "]")
            {
                string expected = token.Text == ")" ? "(" : "[";
                if (stack.Count == 0 || stack.Peek().Text != expected)
                    Add(diagnostics, "DEL001", $"Unexpected closing delimiter '{token.Text}'.", token.Start);
                else
                    stack.Pop();
            }
        }

        foreach (var token in stack)
            Add(diagnostics, "DEL002", $"Opening delimiter '{token.Text}' is not closed.", token.Start);
    }

    private static Token[] Significant(IEnumerable<Token> tokens) =>
        tokens.Where(token => token.Kind is not TokenKind.Trivia and not TokenKind.EndOfFile).ToArray();

    private static IEnumerable<SyntaxBlock> Flatten(IEnumerable<SyntaxBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            foreach (var child in Flatten(block.Children))
                yield return child;
        }
    }

    private static void Add(
        List<GrammarDiagnostic> diagnostics,
        string code,
        string message,
        int position,
        int length = 1) =>
        diagnostics.Add(new GrammarDiagnostic(code, GrammarDiagnosticSeverity.Error,
            message, Math.Max(0, position), Math.Max(1, length)));
}

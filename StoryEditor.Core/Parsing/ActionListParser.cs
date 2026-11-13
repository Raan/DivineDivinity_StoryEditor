using StoryEditor2.Lexing;

namespace StoryEditor2.Parsing;

public sealed record LifecycleActionCall(
    string Name,
    IReadOnlyList<string> Arguments,
    bool IsNegated,
    int Start,
    int Length);

public enum LifecycleActionKind
{
    Call,
    GoalCompleted,
    GoalControl
}

public sealed record LifecycleActionStatement(
    LifecycleActionKind Kind,
    string Name,
    IReadOnlyList<string> Arguments,
    bool IsNegated,
    uint? GoalControlId,
    int Start,
    int Length);

public sealed record ActionListParseResult(
    IReadOnlyList<LifecycleActionCall> Calls,
    IReadOnlyList<ParseDiagnostic> Diagnostics)
{
    public IReadOnlyList<LifecycleActionStatement> Statements { get; init; } =
        Array.Empty<LifecycleActionStatement>();
}

/// <summary>Validates the syntax of one complete Osiris INIT or EXIT section.</summary>
public sealed class ActionListParser
{
    public ActionListParseResult Parse(string source, ApiSymbolCatalog? api = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tokens = Lexer.Tokenize(source)
            .Where(token => token.Kind is not TokenKind.Trivia and not TokenKind.EndOfFile)
            .ToArray();
        var calls = new List<LifecycleActionCall>();
        var statements = new List<LifecycleActionStatement>();
        var diagnostics = new List<ParseDiagnostic>();

        if (tokens.Length < 3 || tokens[0].Kind != TokenKind.Identifier ||
            tokens[0].Text is not ("INIT" or "EXIT") || tokens[1].Text != "{" ||
            tokens[^1].Text != "}")
        {
            diagnostics.Add(new ParseDiagnostic("Expected a complete INIT { ... } or EXIT { ... } section.", 0));
            return new ActionListParseResult(calls, diagnostics) { Statements = statements };
        }

        int index = 2;
        int bodyEnd = tokens.Length - 1;
        while (index < bodyEnd)
        {
            int statementStart = index;
            int depth = 0;
            bool terminated = false;
            while (index < bodyEnd)
            {
                string text = tokens[index].Text;
                if (text == "{")
                {
                    diagnostics.Add(new ParseDiagnostic("Nested blocks are not valid inside INIT/EXIT action lists.", tokens[index].Start));
                    return new ActionListParseResult(calls, diagnostics) { Statements = statements };
                }
                if (text == "}")
                {
                    diagnostics.Add(new ParseDiagnostic("An action is missing its terminating semicolon.", tokens[index].Start));
                    return new ActionListParseResult(calls, diagnostics) { Statements = statements };
                }
                if (text == "(") depth++;
                else if (text == ")")
                {
                    depth--;
                    if (depth < 0)
                    {
                        diagnostics.Add(new ParseDiagnostic("Unexpected closing parenthesis in action.", tokens[index].Start));
                        return new ActionListParseResult(calls, diagnostics) { Statements = statements };
                    }
                }
                else if (text == ";" && depth == 0)
                {
                    terminated = true;
                    break;
                }
                index++;
            }

            if (!terminated)
            {
                diagnostics.Add(new ParseDiagnostic("An action is missing its terminating semicolon.",
                    statementStart < bodyEnd ? tokens[statementStart].Start : source.Length));
                break;
            }

            var statement = tokens[statementStart..index];
            int statementCountBefore = statements.Count;
            ValidateStatement(statement, api, calls, statements, diagnostics);
            if (statements.Count > statementCountBefore)
            {
                var parsedStatement = statements[^1];
                statements[^1] = parsedStatement with
                {
                    Length = tokens[index].End - parsedStatement.Start
                };
            }
            index++; // semicolon
        }

        return new ActionListParseResult(calls, diagnostics) { Statements = statements };
    }

    private static void ValidateStatement(
        IReadOnlyList<Token> statement,
        ApiSymbolCatalog? api,
        List<LifecycleActionCall> calls,
        List<LifecycleActionStatement> statements,
        List<ParseDiagnostic> diagnostics)
    {
        if (statement.Count == 0) return;
        int first = 0;
        bool negated = false;
        if (statement[first].Text == "NOT")
        {
            negated = true;
            first++;
        }

        if (first >= statement.Count)
        {
            diagnostics.Add(new ParseDiagnostic("NOT must be followed by an action call.", statement[0].Start));
            return;
        }

        if (!negated && statement[first].Text == "!")
        {
            if (statement.Count == first + 2 && statement[first + 1].Kind == TokenKind.IntegerLiteral &&
                uint.TryParse(statement[first + 1].Text, out uint goalId))
            {
                statements.Add(new LifecycleActionStatement(
                    LifecycleActionKind.GoalControl, "!", [], false, goalId,
                    statement[0].Start, statement[^1].End - statement[0].Start));
                return;
            }
            diagnostics.Add(new ParseDiagnostic("Expected a numeric Goal control directive such as !1;.", statement[first].Start));
            return;
        }

        if (!negated && statement.Count == 1 && statement[first].Text == "GoalCompleted")
        {
            statements.Add(new LifecycleActionStatement(
                LifecycleActionKind.GoalCompleted, "GoalCompleted", [], false, null,
                statement[0].Start, statement[0].End - statement[0].Start));
            return;
        }

        if (statement[first].Kind != TokenKind.Identifier ||
            first + 1 >= statement.Count || statement[first + 1].Text != "(")
        {
            diagnostics.Add(new ParseDiagnostic("Expected an Osiris action call in the form Name(arguments);.", statement[first].Start));
            return;
        }

        int depth = 0;
        int closeIndex = -1;
        for (int i = first + 1; i < statement.Count; i++)
        {
            if (statement[i].Text == "(") depth++;
            else if (statement[i].Text == ")")
            {
                depth--;
                if (depth == 0) { closeIndex = i; break; }
            }
        }
        if (closeIndex != statement.Count - 1 || depth != 0)
        {
            diagnostics.Add(new ParseDiagnostic("Action call has unbalanced parentheses or trailing tokens.", statement[first].Start));
            return;
        }

        var argumentTokens = SplitArguments(statement, first + 2, closeIndex, diagnostics);
        if (argumentTokens is null) return;
        string name = statement[first].Text;
        var arguments = argumentTokens
            .Select(argument => string.Concat(argument.Select(token => token.Text)))
            .ToArray();
        int end = statement[^1].End;
        calls.Add(new LifecycleActionCall(name, arguments, negated,
            statement[0].Start, end - statement[0].Start));
        statements.Add(new LifecycleActionStatement(
            LifecycleActionKind.Call, name, arguments, negated, null,
            statement[0].Start, end - statement[0].Start));

        // Undeclared names are allowed: INIT/EXIT can assert Story-defined Osiris facts.
        if (api is null || !api.FunctionSignatures.TryGetValue(name, out ApiFunctionSignature? signature))
            return;

        if (signature.Kind is not ApiSymbolKind.Call and not ApiSymbolKind.SystemCall)
        {
            diagnostics.Add(new ParseDiagnostic($"'{name}' is declared as {signature.Kind}, not as an action call.", statement[first].Start));
            return;
        }

        if (arguments.Length != signature.Parameters.Count)
        {
            diagnostics.Add(new ParseDiagnostic(
                $"'{name}' expects {signature.Parameters.Count} argument(s) by its API declaration, but {arguments.Length} were supplied.",
                statement[first].Start));
            return;
        }

        for (int i = 0; i < argumentTokens.Count; i++)
            ValidateLiteralType(name, i, argumentTokens[i], signature.Parameters[i].Type, api, diagnostics);
    }

    private static List<List<Token>>? SplitArguments(
        IReadOnlyList<Token> tokens,
        int start,
        int end,
        List<ParseDiagnostic> diagnostics)
    {
        if (start == end) return [];
        var result = new List<List<Token>>();
        int argumentStart = start;
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Text == "(") depth++;
            else if (tokens[i].Text == ")") depth--;
            else if (tokens[i].Text == "," && depth == 0)
            {
                if (i == argumentStart)
                {
                    diagnostics.Add(new ParseDiagnostic("Empty action argument.", tokens[i].Start));
                    return null;
                }
                result.Add(tokens.Skip(argumentStart).Take(i - argumentStart).ToList());
                argumentStart = i + 1;
            }
        }
        if (argumentStart == end)
        {
            diagnostics.Add(new ParseDiagnostic("Action call has an empty trailing argument.", tokens[end - 1].Start));
            return null;
        }
        result.Add(tokens.Skip(argumentStart).Take(end - argumentStart).ToList());
        return result;
    }

    private static void ValidateLiteralType(
        string functionName,
        int argumentIndex,
        IReadOnlyList<Token> argument,
        string expectedType,
        ApiSymbolCatalog api,
        List<ParseDiagnostic> diagnostics)
    {
        Token? literal = argument.Count == 1 ? argument[0] : null;
        bool isSignedNumber = argument.Count == 2 && (argument[0].Text is "+" or "-") &&
            argument[1].Kind is TokenKind.IntegerLiteral or TokenKind.RealLiteral;
        Token? signedValue = isSignedNumber ? argument[1] : null;
        string? actualObjectType = literal?.Kind == TokenKind.Identifier &&
            api.ObjectTypes.TryGetValue(literal.Text, out string? objectType) ? objectType : null;

        bool mismatch = expectedType switch
        {
            "INTEGER" => literal?.Kind == TokenKind.RealLiteral || literal?.Kind == TokenKind.StringLiteral ||
                          signedValue?.Kind == TokenKind.RealLiteral || actualObjectType is not null,
            "REAL" => literal?.Kind == TokenKind.StringLiteral || actualObjectType is not null,
            "STRING" => literal?.Kind is TokenKind.IntegerLiteral or TokenKind.RealLiteral ||
                        signedValue is not null || actualObjectType is not null,
            _ => actualObjectType is not null
                ? !string.Equals(actualObjectType, expectedType, StringComparison.Ordinal)
                : literal?.Kind is TokenKind.IntegerLiteral or TokenKind.RealLiteral or TokenKind.StringLiteral || signedValue is not null
        };

        if (mismatch)
            diagnostics.Add(new ParseDiagnostic(
                $"'{functionName}' argument {argumentIndex + 1} expects {expectedType}, but the supplied literal/object has an incompatible type.",
                argument[0].Start));
    }
}

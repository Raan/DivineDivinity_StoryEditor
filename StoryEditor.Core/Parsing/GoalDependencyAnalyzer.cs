using System.Text;
using StoryEditor2.Lexing;

namespace StoryEditor2.Parsing;

public enum GoalDependencyKind
{
    Activates,
    Completes,
    SetsSleeping,
    ChecksActive,
    ChecksCompleted,
    ChecksSleeping,
    ReadsStatus,
    ChecksWasActive,
    ChecksWasCompleted,
    ChecksWasDefined,
    ChecksWasSleeping,
    InvokesProcedure
}

public enum GoalDependencyConfidence
{
    ApiDeclared,
    StoryProcedureHandler,
    NameBasedCandidate
}

public sealed record GoalProcedureHandler(
    GoalSymbol OwnerGoal,
    string TargetText,
    int RuleStart,
    int RuleLength,
    string EffectSummary);

public sealed record GoalDependencyReference(
    GoalSymbol SourceGoal,
    GoalSymbol? TargetGoal,
    string TargetText,
    string FunctionName,
    GoalDependencyKind Kind,
    GoalDependencyConfidence Confidence,
    string SectionName,
    RuleKind? RuleKind,
    int RuleStart,
    int RuleLength,
    int CallStart,
    int CallLength,
    int ArgumentStart,
    bool IsCondition,
    string? HandlerSummary = null,
    int? HandlerRuleStart = null,
    int? HandlerRuleLength = null)
{
    public bool IsResolved => TargetGoal is not null;
}

public sealed record GoalDependencyAnalysisResult(
    IReadOnlyList<GoalDependencyReference> References,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    IReadOnlyList<GoalProcedureHandler> ProcedureHandlers)
{
    public int ResolvedCount => References.Count(r => r.IsResolved);
    public int ConfirmedCount => References.Count(r =>
        r.IsResolved && r.Confidence == GoalDependencyConfidence.ApiDeclared);
    public int ProcedureCount => References.Count(r =>
        r.IsResolved && r.Confidence == GoalDependencyConfidence.StoryProcedureHandler);
    public int CandidateCount => References.Count(r =>
        r.IsResolved && r.Confidence == GoalDependencyConfidence.NameBasedCandidate);
    public int UnresolvedCount => References.Count(r => !r.IsResolved);
}

/// <summary>
/// Builds a source-level Goal dependency index from declared Goal API operations
/// and actual calls to Story.div Osiris procedures. PROC rule heads are indexed
/// as handlers, never mistaken for calls. This is not a runtime execution graph.
/// </summary>
public sealed class GoalDependencyAnalyzer
{
    private const string CompletionProcedureName = "PROC_CompleteGoal";

    private sealed record Operation(GoalDependencyKind Kind);

    private static readonly IReadOnlyDictionary<string, Operation> Operations =
        new Dictionary<string, Operation>(StringComparer.OrdinalIgnoreCase)
        {
            ["ActivateGoal"] = new(GoalDependencyKind.Activates),
            ["CompleteGoal"] = new(GoalDependencyKind.Completes),
            ["SetGoalSleeping"] = new(GoalDependencyKind.SetsSleeping),
            ["IsActive"] = new(GoalDependencyKind.ChecksActive),
            ["IsCompleted"] = new(GoalDependencyKind.ChecksCompleted),
            ["IsSleeping"] = new(GoalDependencyKind.ChecksSleeping),
            ["Status"] = new(GoalDependencyKind.ReadsStatus),
            ["WasActive"] = new(GoalDependencyKind.ChecksWasActive),
            ["WasCompleted"] = new(GoalDependencyKind.ChecksWasCompleted),
            ["WasDefined"] = new(GoalDependencyKind.ChecksWasDefined),
            ["WasSleeping"] = new(GoalDependencyKind.ChecksWasSleeping)
        };

    public GoalDependencyAnalysisResult Analyze(
        string source,
        GoalCatalog catalog,
        IReadOnlyDictionary<int, string>? editedKnowledgeBases = null)
    {
        var diagnostics = new List<ParseDiagnostic>();
        var goalsByTitle = BuildTitleIndex(catalog);
        var handlers = BuildProcedureHandlers(source, catalog, editedKnowledgeBases);
        var handlersByArgument = BuildHandlerIndex(handlers);
        var references = new List<GoalDependencyReference>();

        foreach (var goal in catalog.Goals)
        {
            string? editedText = editedKnowledgeBases is not null &&
                editedKnowledgeBases.TryGetValue(goal.Id, out var value) ? value : null;
            var oneGoal = AnalyzeGoalCore(
                source, goal, goalsByTitle, handlersByArgument, editedText);
            references.AddRange(oneGoal.References);
            diagnostics.AddRange(oneGoal.Diagnostics);
        }

        return CreateResult(references, diagnostics, handlers);
    }

    public GoalDependencyAnalysisResult AnalyzeGoal(
        string source,
        GoalCatalog catalog,
        GoalSymbol goal,
        string? editedKnowledgeBase = null,
        IReadOnlyList<GoalProcedureHandler>? existingHandlers = null)
    {
        var goalsByTitle = BuildTitleIndex(catalog);
        var handlers = existingHandlers ?? BuildProcedureHandlers(
            source, catalog, editedKnowledgeBases: null);
        var oneGoal = AnalyzeGoalCore(
            source, goal, goalsByTitle, BuildHandlerIndex(handlers), editedKnowledgeBase);

        return CreateResult(oneGoal.References, oneGoal.Diagnostics, handlers);
    }

    private static IReadOnlyList<GoalProcedureHandler> BuildProcedureHandlers(
        string source,
        GoalCatalog catalog,
        IReadOnlyDictionary<int, string>? editedKnowledgeBases)
    {
        var handlers = new List<GoalProcedureHandler>();

        foreach (var goal in catalog.Goals)
        {
            var kb = goal.Block.Children.FirstOrDefault(
                b => b.Kind == SyntaxKind.KnowledgeBaseSection);
            if (kb is null) continue;

            string kbText = editedKnowledgeBases is not null &&
                editedKnowledgeBases.TryGetValue(goal.Id, out var edited)
                    ? edited
                    : source.Substring(kb.Start, kb.Length);

            var tokens = Lexer.Tokenize(kbText);
            var structure = new StructureParser().Parse(kbText, tokens);
            var parsed = new RuleParser().Parse(kbText, tokens, structure.Blocks);

            foreach (var rule in parsed.Rules)
            {
                var head = FindCompletionProcedureHead(rule);
                if (head is null || head.Arguments.Count == 0 ||
                    !TryReadStringLiteral(head.Arguments[0].Text, out string targetText))
                    continue;

                handlers.Add(new GoalProcedureHandler(
                    goal,
                    targetText,
                    kb.Start + rule.Start,
                    rule.Length,
                    SummarizeHandler(rule)));
            }
        }

        return handlers
            .OrderBy(h => h.OwnerGoal.Id)
            .ThenBy(h => h.RuleStart)
            .ToArray();
    }

    private static Dictionary<string, GoalProcedureHandler[]> BuildHandlerIndex(
        IReadOnlyList<GoalProcedureHandler> handlers) =>
        handlers
            .GroupBy(h => h.TargetText, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

    private static FunctionCallExpression? FindCompletionProcedureHead(OsirisRule rule)
    {
        if (rule.Kind != RuleKind.Proc || rule.Conditions.Count == 0)
            return null;

        var firstCondition = rule.Conditions[0];
        return rule.Calls
            .Where(call => string.Equals(
                call.Name, CompletionProcedureName, StringComparison.OrdinalIgnoreCase))
            .Where(call => call.Start >= firstCondition.Start &&
                call.Start < firstCondition.Start + firstCondition.Length)
            .OrderBy(call => call.Start)
            .FirstOrDefault();
    }

    private static string SummarizeHandler(OsirisRule rule)
    {
        var actions = rule.Actions.Select(a => a.Text.Trim()).ToArray();
        var summaries = new List<string>();

        if (actions.Any(a => ContainsCall(a, "GoalCompleted")))
            summaries.Add("GoalCompleted: completes the handler's owning Goal");
        if (actions.Any(a => ContainsCall(a, "ExitGoal")))
            summaries.Add("ExitGoal: exits a Goal");
        if (actions.Any(a => ContainsCall(a, CompletionProcedureName)))
            summaries.Add("chains additional PROC_CompleteGoal calls");

        if (summaries.Count == 0)
        {
            foreach (var action in actions.Take(3))
            {
                string name = action.Split(new[] { '(', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? action;
                summaries.Add(name);
            }
        }

        if (summaries.Count == 0)
            return "no parsed action body";

        return string.Join("; ", summaries.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool ContainsCall(string text, string name) =>
        text.Contains(name + "(", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, GoalSymbol> BuildTitleIndex(GoalCatalog catalog) =>
        catalog.Goals
            .Where(g => !string.IsNullOrWhiteSpace(g.Title))
            .GroupBy(g => NormalizeGoalName(DecodeGoalText(g.Title!)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static GoalDependencyAnalysisResult AnalyzeGoalCore(
        string source,
        GoalSymbol goal,
        IReadOnlyDictionary<string, GoalSymbol> goalsByTitle,
        IReadOnlyDictionary<string, GoalProcedureHandler[]> handlersByArgument,
        string? editedKnowledgeBase)
    {
        var references = new List<GoalDependencyReference>();
        var diagnostics = new List<ParseDiagnostic>();

        var kb = goal.Block.Children.FirstOrDefault(
            b => b.Kind == SyntaxKind.KnowledgeBaseSection);
        if (kb is not null)
        {
            string kbText = editedKnowledgeBase ?? source.Substring(kb.Start, kb.Length);
            var tokens = Lexer.Tokenize(kbText);
            var structure = new StructureParser().Parse(kbText, tokens);
            var parsed = new RuleParser().Parse(kbText, tokens, structure.Blocks);

            diagnostics.AddRange(structure.Diagnostics.Select(d =>
                new ParseDiagnostic($"Goal {goal.Id}: {d.Message}", kb.Start + d.Position)));
            diagnostics.AddRange(parsed.Diagnostics.Select(d =>
                new ParseDiagnostic($"Goal {goal.Id}: {d.Message}", kb.Start + d.Position)));

            foreach (var rule in parsed.Rules)
            {
                var procedureHead = FindCompletionProcedureHead(rule);

                foreach (var call in rule.Calls)
                {
                    if (procedureHead is not null && call.Start == procedureHead.Start)
                        continue;

                    if (call.Arguments.Count == 0)
                        continue;

                    var firstArgument = call.Arguments[0];
                    if (!TryReadStringLiteral(firstArgument.Text, out string targetText))
                        continue;

                    bool isCondition = rule.Conditions.Any(condition =>
                        call.Start >= condition.Start &&
                        call.Start < condition.Start + condition.Length);

                    if (string.Equals(call.Name, CompletionProcedureName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        AddProcedureReference(
                            references, goalsByTitle, handlersByArgument, goal,
                            targetText, "KB", rule.Kind, rule.Start, rule.Length,
                            call.Start, call.Length, firstArgument.Start, isCondition);
                        continue;
                    }

                    if (!Operations.TryGetValue(call.Name, out var operation))
                        continue;

                    AddApiReference(
                        references, goalsByTitle, goal, targetText, call.Name,
                        operation, "KB", rule.Kind, rule.Start, rule.Length,
                        call.Start, call.Length, firstArgument.Start, isCondition);
                }
            }
        }

        foreach (var sectionKind in new[] { SyntaxKind.InitSection, SyntaxKind.ExitSection })
        {
            var section = goal.Block.Children.FirstOrDefault(b => b.Kind == sectionKind);
            if (section is null) continue;

            string sectionName = sectionKind == SyntaxKind.InitSection ? "INIT" : "EXIT";
            string sectionText = source.Substring(section.Start, section.Length);

            foreach (var call in ParseSectionCalls(sectionText))
            {
                if (call.Arguments.Count == 0 ||
                    !TryReadStringLiteral(call.Arguments[0].Text, out string targetText))
                    continue;

                var firstArgument = call.Arguments[0];

                if (string.Equals(call.Name, CompletionProcedureName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    AddProcedureReference(
                        references, goalsByTitle, handlersByArgument, goal,
                        targetText, sectionName, null, call.Start, call.Length,
                        call.Start, call.Length, firstArgument.Start, false);
                    continue;
                }

                if (!Operations.TryGetValue(call.Name, out var operation))
                    continue;

                AddApiReference(
                    references, goalsByTitle, goal, targetText, call.Name,
                    operation, sectionName, null, call.Start, call.Length,
                    call.Start, call.Length, firstArgument.Start, false);
            }
        }

        return CreateResult(references, diagnostics, Array.Empty<GoalProcedureHandler>());
    }

    private static void AddApiReference(
        List<GoalDependencyReference> references,
        IReadOnlyDictionary<string, GoalSymbol> goalsByTitle,
        GoalSymbol sourceGoal,
        string targetText,
        string functionName,
        Operation operation,
        string sectionName,
        RuleKind? ruleKind,
        int ruleStart,
        int ruleLength,
        int callStart,
        int callLength,
        int argumentStart,
        bool isCondition)
    {
        goalsByTitle.TryGetValue(NormalizeGoalName(targetText), out var targetGoal);

        references.Add(new GoalDependencyReference(
            sourceGoal, targetGoal, targetText, functionName, operation.Kind,
            GoalDependencyConfidence.ApiDeclared, sectionName, ruleKind,
            ruleStart, ruleLength, callStart, callLength, argumentStart, isCondition));
    }

    private static void AddProcedureReference(
        List<GoalDependencyReference> references,
        IReadOnlyDictionary<string, GoalSymbol> goalsByTitle,
        IReadOnlyDictionary<string, GoalProcedureHandler[]> handlersByArgument,
        GoalSymbol sourceGoal,
        string targetText,
        string sectionName,
        RuleKind? ruleKind,
        int ruleStart,
        int ruleLength,
        int callStart,
        int callLength,
        int argumentStart,
        bool isCondition)
    {
        string normalized = NormalizeGoalName(targetText);
        if (handlersByArgument.TryGetValue(normalized, out var handlers))
        {
            foreach (var group in handlers.GroupBy(h => h.OwnerGoal.Id))
            {
                var first = group.First();
                string summary = string.Join(" | ",
                    group.Select(h => h.EffectSummary)
                        .Distinct(StringComparer.OrdinalIgnoreCase));

                references.Add(new GoalDependencyReference(
                    sourceGoal,
                    first.OwnerGoal,
                    targetText,
                    CompletionProcedureName,
                    GoalDependencyKind.InvokesProcedure,
                    GoalDependencyConfidence.StoryProcedureHandler,
                    sectionName,
                    ruleKind,
                    ruleStart,
                    ruleLength,
                    callStart,
                    callLength,
                    argumentStart,
                    isCondition,
                    summary,
                    first.RuleStart,
                    first.RuleLength));
            }
            return;
        }

        goalsByTitle.TryGetValue(normalized, out var titleMatch);
        GoalSymbol? looseTitleMatch = titleMatch is null
            ? goalsByTitle.Values.FirstOrDefault(candidate =>
                NormalizeLooseGoalName(DecodeGoalText(candidate.Title ?? string.Empty)) ==
                NormalizeLooseGoalName(targetText))
            : null;
        string unresolvedSummary = looseTitleMatch is null
            ? "no matching PROC_CompleteGoal handler or Goal title was found"
            : $"no exact handler; possible title alias is Goal({looseTitleMatch.Id}) '{looseTitleMatch.Title}'";
        references.Add(new GoalDependencyReference(
            sourceGoal,
            titleMatch,
            targetText,
            CompletionProcedureName,
            GoalDependencyKind.InvokesProcedure,
            GoalDependencyConfidence.NameBasedCandidate,
            sectionName,
            ruleKind,
            ruleStart,
            ruleLength,
            callStart,
            callLength,
            argumentStart,
            isCondition,
            titleMatch is null
                ? unresolvedSummary
                : "Goal title matches, but no exact PROC_CompleteGoal handler was found"));
    }

    private static IReadOnlyList<FunctionCallExpression> ParseSectionCalls(string source)
    {
        var tokens = Lexer.Tokenize(source)
            .Where(t => t.Kind != TokenKind.Trivia && t.Kind != TokenKind.EndOfFile)
            .ToArray();
        var calls = new List<FunctionCallExpression>();

        for (int i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i].Kind != TokenKind.Identifier || tokens[i + 1].Text != "(")
                continue;

            int close = FindMatchingParen(tokens, i + 1);
            if (close < 0) continue;

            var arguments = new List<CallArgument>();
            int argumentStart = i + 2;
            int depth = 0;
            for (int j = argumentStart; j <= close; j++)
            {
                string value = j < close ? tokens[j].Text : ")";
                if (value == "(") depth++;
                else if (value == ")") depth--;

                if (j == close || (value == "," && depth == 0))
                {
                    if (argumentStart < j)
                    {
                        int start = tokens[argumentStart].Start;
                        int end = tokens[j - 1].End;
                        arguments.Add(new CallArgument(source[start..end], start, end - start));
                    }
                    argumentStart = j + 1;
                }
            }

            int callStart = tokens[i].Start;
            int callEnd = tokens[close].End;
            calls.Add(new FunctionCallExpression(tokens[i].Text, callStart,
                callEnd - callStart, arguments));
        }

        return calls;
    }

    private static int FindMatchingParen(Token[] tokens, int open)
    {
        int depth = 0;
        for (int i = open; i < tokens.Length; i++)
        {
            if (tokens[i].Text == "(") depth++;
            else if (tokens[i].Text == ")" && --depth == 0) return i;
        }
        return -1;
    }

    private static GoalDependencyAnalysisResult CreateResult(
        IEnumerable<GoalDependencyReference> references,
        IEnumerable<ParseDiagnostic> diagnostics,
        IReadOnlyList<GoalProcedureHandler> handlers) =>
        new(
            references.OrderBy(r => r.SourceGoal.Id)
                .ThenBy(r => r.SectionName, StringComparer.Ordinal)
                .ThenBy(r => r.RuleStart)
                .ThenBy(r => r.CallStart)
                .ToArray(),
            diagnostics.ToArray(),
            handlers);

    private static string NormalizeGoalName(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string NormalizeLooseGoalName(string value) =>
        NormalizeGoalName(value.Replace('_', ' '));
    private static string DecodeGoalText(string value) =>
        TryReadStringLiteral("\"" + value + "\"", out var decoded) ? decoded : value;

    private static bool TryReadStringLiteral(string text, out string value)
    {
        value = string.Empty;
        string trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
            return false;

        var result = new StringBuilder(trimmed.Length - 2);
        for (int i = 1; i < trimmed.Length - 1; i++)
        {
            char current = trimmed[i];
            if (current == '\\' && i + 1 < trimmed.Length - 1)
            {
                char escaped = trimmed[++i];
                result.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped
                });
            }
            else
            {
                result.Append(current);
            }
        }

        value = result.ToString();
        return true;
    }
}

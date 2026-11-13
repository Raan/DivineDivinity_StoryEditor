using StoryCompiler;
using StoryEditor2.Lexing;
using StoryEditor2.Parsing;

var tests = new List<(string Name, Action Run)>
{
    ("Lexer keeps braces and semicolons inside strings", LexerKeepsStringContents),
    ("Structure parser finds nested Goal and KB blocks", StructureParserFindsGoalAndKb),
    ("Rule parser reads IF, AND NOT and multiple actions", RuleParserReadsIfAndNotAndActions),
    ("Rule condition negation and connectors stay synchronized", RuleConditionSemanticsStaySynchronized),
    ("Rule trivia layouts preserve exact whitespace and comments", RuleTriviaLayoutsPreserveExactGaps),
    ("Rule parser preserves typed PROC signature", RuleParserReadsProcSignature),
    ("PROC wildcard slots retain typed signatures without binding body variables", ProcHeaderWildcardsRemainTypedAndUnbound),
    ("Save-padded adaptor mappings are detected", SavePaddedAdaptorsAreDetected),
    ("INIT/EXIT control records render as Osiris directives", ActionListControlRecordsRenderCorrectly),
    ("Lifecycle action parser validates syntax and declared API signatures", ActionListParserValidatesCallsAndSignatures),
    ("Lifecycle action list edits preserve comments and source formatting", LifecycleActionListEditsPreserveTrivia),
    ("Non-empty global action section produces a diagnostic", GlobalActionSectionIsNotSilentlyDiscarded),
    ("Rule parser reports missing THEN", RuleParserReportsMissingThen),
    ("Osiris grammar validator accepts conditions, comparisons and PROC headers", GrammarValidatorAcceptsConfirmedSyntax),
    ("Osiris grammar validator detects malformed calls and unclosed comments", GrammarValidatorDetectsMalformedSyntax),
    ("A missing condition parenthesis does not cascade into hundreds of errors", GrammarValidatorRecoversAfterMissingConditionParenthesis),
    ("Parser source spans remain inside original text", ParserSpansStayInsideSource)
};

string? fullSourcePath = Environment.GetEnvironmentVariable("STORYEDITOR_TEST_SOURCE");
if (!string.IsNullOrWhiteSpace(fullSourcePath))
    tests.Add(("Full game Story passes Osiris grammar validation", () => FullStoryPassesGrammarValidation(fullSourcePath)));

string? binaryStoryPath = Environment.GetEnvironmentVariable("STORYEDITOR_TEST_STORY");
if (!string.IsNullOrWhiteSpace(binaryStoryPath))
    tests.Add(("Binary Story decompiles without losing Rete action nodes", () => BinaryStoryDecompiles(binaryStoryPath)));

int failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"\nRegression tests: {tests.Count - failures}/{tests.Count} passed.");
return failures == 0 ? 0 : 1;

static void LexerKeepsStringContents()
{
    const string source = "IF StringTable(\"brace { and ; inside\", 1) THEN NOP(1);";
    var tokens = Lexer.Tokenize(source);
    Assert(tokens.Any(t => t.Kind == TokenKind.StringLiteral &&
        t.Text == "\"brace { and ; inside\""), "Quoted text was not emitted as one string token.");
    Assert(tokens.Count(t => t.Text == "{") == 0, "Brace inside a string became a structural token.");
    Assert(tokens.Count(t => t.Text == ";") == 1, "Semicolon inside a string became a statement terminator.");
}

static void StructureParserFindsGoalAndKb()
{
    const string source = """
        Goal(1).Title("Regression { ; }");
        Goal(1) {
            KB {
                IF StringTable("x", 1) THEN NOP(1);
            }
        }
        """;
    var tokens = Lexer.Tokenize(source);
    var result = new StructureParser().Parse(source, tokens);
    Assert(result.Diagnostics.Count == 0, "Unexpected structure diagnostics.");
    var goal = result.Blocks.Single(b => b.Kind == SyntaxKind.GoalBlock);
    Assert(goal.Children.Any(b => b.Kind == SyntaxKind.KnowledgeBaseSection),
        "Goal does not contain a KB section.");
}

static void RuleParserReadsIfAndNotAndActions()
{
    const string source = """
        Goal(1) {
            KB {
                IF
                    StringTable("x", 1)
                    AND NOT StringTable("y", 2)
                THEN
                    NOP(1);
                    NOP(2);
            }
        }
        """;
    var (tokens, structure) = ParseStructure(source);
    var result = new RuleParser().Parse(source, tokens, structure.Blocks);
    Assert(result.Diagnostics.Count == 0, "Unexpected rule diagnostics.");
    var rule = result.Rules.Single();
    Assert(rule.Kind == RuleKind.If, "Expected IF rule.");
    Assert(rule.Conditions.Count == 2, $"Expected 2 conditions, got {rule.Conditions.Count}.");
    Assert(rule.Conditions[1].Connector == RuleConditionConnector.AndNot,
        "Second condition did not retain AND NOT.");
    Assert(rule.Actions.Count == 2, $"Expected 2 actions, got {rule.Actions.Count}.");
}

static void RuleConditionSemanticsStaySynchronized()
{
    Assert(RuleConditionSemantics.ConnectorFor(false, 0) == RuleConditionConnector.First,
        "The first condition must use the leading-condition form.");
    Assert(RuleConditionSemantics.ConnectorFor(true, 0) == RuleConditionConnector.First,
        "Negating the first condition must not turn it into an AND connector.");
    Assert(RuleConditionSemantics.ConnectorFor(false, 1) == RuleConditionConnector.And,
        "A positive later condition must use AND.");
    Assert(RuleConditionSemantics.ConnectorFor(true, 1) == RuleConditionConnector.AndNot,
        "A negated later condition must use AND NOT.");
    Assert(RuleConditionSemantics.IsNegated(RuleConditionConnector.First, true, 0),
        "Leading NOT was not recognized.");
    Assert(RuleConditionSemantics.IsNegated(RuleConditionConnector.AndNot, false, 1),
        "AND NOT was not recognized as negation.");
    Assert(!RuleConditionSemantics.IsNegated(RuleConditionConnector.And, true, 1),
        "A later condition's stale leading-negation flag must not override its connector.");
}

static void RuleTriviaLayoutsPreserveExactGaps()
{
    string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CommentedRules.div");
    string source = File.ReadAllText(fixturePath);
    var (tokens, structure) = ParseStructure(source);
    var parsed = new RuleParser().Parse(source, tokens, structure.Blocks);
    Assert(parsed.Diagnostics.Count == 0, "Comment fixture produced parser diagnostics.");
    var rule = parsed.Rules.Single();
    Assert(rule.Conditions.Count == 3 && rule.Actions.Count == 3,
        "Comment fixture did not produce the expected condition/action counts.");

    var conditionLayouts = RuleTriviaPreserver.ExtractConditionLayouts(source,
        rule.Conditions.Select(item => (item.Start, item.Length)));
    Assert(conditionLayouts.Count == 3, "Condition layout count did not match parsed elements.");
    Assert(conditionLayouts[1].Contains("\n\n", StringComparison.Ordinal),
        "The blank line before the second condition was not preserved.");
    Assert(conditionLayouts[1].Contains("// comment belongs to the second condition", StringComparison.Ordinal),
        "The line comment was not preserved in its original gap.");
    Assert(conditionLayouts[1].Contains(RuleTriviaPreserver.ConnectorMarker, StringComparison.Ordinal),
        "The AND NOT connector placeholder was not retained.");
    Assert(conditionLayouts[2].Contains(RuleTriviaPreserver.ConnectorMarker, StringComparison.Ordinal),
        "The standalone AND connector placeholder was not retained.");
    string movedConditionLayout = conditionLayouts[1]
        .Replace(RuleTriviaPreserver.ConnectorMarker, "AND NOT", StringComparison.Ordinal);
    Assert(movedConditionLayout.Contains("\n\n", StringComparison.Ordinal) &&
        movedConditionLayout.Contains("// comment belongs to the second condition", StringComparison.Ordinal),
        "Moving a condition would lose its exact leading trivia.");

    var actionLayouts = RuleTriviaPreserver.ExtractActionLayouts(source,
        rule.Actions.Select(item => (item.Start, item.Length)));
    Assert(actionLayouts.Count == 3, "Action layout count did not match parsed elements.");
    Assert(actionLayouts[1].Contains("\n\n", StringComparison.Ordinal),
        "The blank line before the second action was not preserved.");
    Assert(actionLayouts[1].Contains("/* comment belongs to the second action */", StringComparison.Ordinal),
        "The block comment was not preserved in its original gap.");
    Assert(actionLayouts[1].Contains(RuleTriviaPreserver.SeparatorMarker, StringComparison.Ordinal),
        "The action separator placeholder was not retained.");
}

static void RuleParserReadsProcSignature()
{
    const string source = """
        Goal(2) {
            KB {
                PROC UpdateTime((INTEGER)_Var1, (INTEGER)_Var2)
                AND
                Time(_Var3, _Var4, _Var5)
                THEN
                NOP(1);
            }
        }
        """;
    var (tokens, structure) = ParseStructure(source);
    var result = new RuleParser().Parse(source, tokens, structure.Blocks);
    Assert(result.Diagnostics.Count == 0, "Unexpected PROC diagnostics.");
    var rule = result.Rules.Single();
    Assert(rule.Kind == RuleKind.Proc, "Expected PROC rule.");
    Assert(rule.ProcedureSignature == "UpdateTime((INTEGER)_Var1, (INTEGER)_Var2)",
        $"Unexpected PROC signature: {rule.ProcedureSignature}");
    Assert(rule.Conditions.Count == 1, "PROC signature was incorrectly parsed as a condition.");
    Assert(rule.Actions.Count == 1, "Expected one PROC action.");
}

static void ProcHeaderWildcardsRemainTypedAndUnbound()
{
    var wildcardAdaptor = new StoryAdaptorRecord(1, [], [byte.MaxValue, 0, 0, 0, 0], []);
    var procFunction = new StoryFunctionRecord(0, 0, 0, 0, 100, 5, 0, 0, 0, 0,
        "ProcessObjectTheftEvent", [], [4, 4, 5, 1]);
    var mixedHeader = new StoryAdaptorRecord(4, [], [byte.MaxValue, byte.MaxValue, 2, 3],
        [new StoryVariableBinding(2, 2), new StoryVariableBinding(3, 3)]);
    var reserved = new HashSet<int> { 0, 1, 2, 3 };
    var rendered = StoryDivDecompiler.RenderAdaptorArguments(
        mixedHeader, procFunction, StoryDivDecompiler.GetUsedProcVariableIds(mixedHeader),
        isProcHeader: true, reservedVariableIds: reserved);
    Assert(rendered.SequenceEqual(["(NPC)_Var5", "(NPC)_Var6", "(OBJECT)_Var3", "(INTEGER)_Var4"]),
        $"PROC wildcard type rendering changed: {string.Join(", ", rendered)}");
    Assert(StoryDivDecompiler.GetUsedProcVariableIds(wildcardAdaptor).Count == 0,
        "Wildcard slots were incorrectly added to the set of body-bound PROC variables.");

    var paddedHeader = new StoryAdaptorRecord(5, [], [byte.MaxValue, 0, 0, 0, 0], []);
    var singleParameterProc = new StoryFunctionRecord(0, 0, 0, 0, 101, 5, 0, 0, 0, 0,
        "SingleParameterProc", [], [1]);
    var paddedRendered = StoryDivDecompiler.RenderAdaptorArguments(
        paddedHeader, singleParameterProc, new HashSet<int>(), isProcHeader: true,
        reservedVariableIds: new HashSet<int> { 0, 1, 2, 3, 4 });
    Assert(paddedRendered.SequenceEqual(["(INTEGER)_Var6"]),
        "Save-padded PROC adaptors must use the typed function signature as their true arity.");

    var mixedAdaptor = new StoryAdaptorRecord(2, [], [byte.MaxValue, 1, byte.MaxValue, 0, 0],
        [new StoryVariableBinding(1, 1)]);
    Assert(StoryDivDecompiler.GetUsedProcVariableIds(mixedAdaptor).SetEquals([1]),
        "The PROC variable set should contain only explicitly bound variable IDs.");

    var repeatedAdaptor = new StoryAdaptorRecord(3, [], [0, 0, 1, 0, 0],
        [new StoryVariableBinding(0, 0), new StoryVariableBinding(1, 2)]);
    Assert(StoryDivDecompiler.GetUsedProcVariableIds(repeatedAdaptor).SetEquals([0, 1]),
        "Repeated variable occurrences should not be confused with zero-padding.");
}
static void SavePaddedAdaptorsAreDetected()
{
    var padded = Enumerable.Range(0, 1_000)
        .Select(i => new StoryAdaptorRecord((uint)i, [], [0, 0, 0, 0, 0], []))
        .ToList();
    Assert(StoryDivDecompiler.HasLikelySavePaddedAdaptorMappings(padded),
        "Canonical five-column zero padding was not detected.");

    var originalWidths = Enumerable.Range(0, 1_000)
        .Select(i => new StoryAdaptorRecord((uint)i, [], Enumerable.Repeat((byte)0, i % 5).ToArray(), []))
        .ToList();
    Assert(!StoryDivDecompiler.HasLikelySavePaddedAdaptorMappings(originalWidths),
        "Variable-width original adaptors were incorrectly classified as padded.");
}
static void ActionListControlRecordsRenderCorrectly()
{
    var integer = new StoryTypedValue(0x31, 1, 1, null, null);
    var actions = new[]
    {
        new StoryActionRecord(0, "", null, [], null, 1),
        new StoryActionRecord(1, "", null, [], null, uint.MaxValue),
        new StoryActionRecord(2, "", null, [], null, 0),
        new StoryActionRecord(3, "NOP", 1,
            [new StoryParameterRecord(0, 0, integer, [], [])], 1, 0)
    };

    string[] rendered = StoryDivDecompiler.RenderActionList(actions).ToArray();
    Assert(rendered.SequenceEqual(["GoalCompleted;", "!1;", "NOT NOP(1);"]),
        $"Unexpected INIT/EXIT action rendering: {string.Join(" | ", rendered)}");
}

static void ActionListParserValidatesCallsAndSignatures()
{
    const string source = """
        type { NPC, 4 }
        object { NPC_Test, 4, ( 4, 1, 0, 0 ) }
        call SetHitpoints([in](NPC)_Arg0, [in](INTEGER)_Arg1) (4, 0, 1, 0)
        INIT {
            SetHitpoints(NPC_Test, 42);
            StoryDefinedFact(1, "text; inside string");
            NOT StoryDefinedFact(2);
            GoalCompleted;
            !1;
        }
        """;

    var api = ApiSymbolCatalog.Build(source);
    Assert(api.FunctionSignatures.TryGetValue("SetHitpoints", out var signature),
        "The API declaration signature was not indexed.");
    Assert(signature!.Parameters.Select(parameter => parameter.Type)
        .SequenceEqual(["NPC", "INTEGER"]), "API parameter types were not parsed correctly.");
    Assert(api.ObjectTypes.TryGetValue("NPC_Test", out var objectType) && objectType == "NPC",
        "The declared object type was not indexed.");

    var parser = new ActionListParser();
    string validSource = source[(source.IndexOf("INIT {", StringComparison.Ordinal))..];
    var valid = parser.Parse(validSource, api);
    Assert(valid.Diagnostics.Count == 0,
        "Valid lifecycle actions were rejected: " + string.Join(" | ", valid.Diagnostics.Select(d => d.Message)));
    Assert(valid.Calls.Count == 3, $"Expected three function calls, got {valid.Calls.Count}.");
    Assert(valid.Calls[2].IsNegated, "NOT modifier was not preserved.");
    Assert(valid.Statements.Count == 5,
        $"Expected all five lifecycle statements, including control directives, got {valid.Statements.Count}.");
    Assert(valid.Statements.Select(statement => statement.Kind).SequenceEqual([
        LifecycleActionKind.Call,
        LifecycleActionKind.Call,
        LifecycleActionKind.Call,
        LifecycleActionKind.GoalCompleted,
        LifecycleActionKind.GoalControl
    ]), "Lifecycle statements were not retained in source order with their distinct kinds.");
    foreach (var statement in valid.Statements)
    {
        Assert(statement.Start >= 0 && statement.Length > 0 &&
            statement.Start + statement.Length <= validSource.Length,
            "Lifecycle statement span is outside the source.");
        string statementSource = validSource.Substring(statement.Start, statement.Length);
        Assert(statementSource.Length == statement.Length,
            "Lifecycle statement source span could not be sliced.");
        Assert(statementSource.EndsWith(';'),
            "Lifecycle statement span must include its terminating semicolon.");
    }
    Assert(valid.Statements[2].IsNegated, "The statement model lost the NOT modifier.");
    Assert(valid.Statements[4].GoalControlId == 1, "The !1; directive did not retain its Goal ID.");

    const string invalid = "INIT { SetHitpoints(\"not an NPC\", 4.5); }";
    var rejected = parser.Parse(invalid, api);
    Assert(rejected.Diagnostics.Count >= 2,
        "Mismatched API literal types were not diagnosed.");

    var missingSemicolon = parser.Parse("EXIT { SetHitpoints(NPC_Test, 1) }", api);
    Assert(missingSemicolon.Diagnostics.Any(d => d.Message.Contains("semicolon", StringComparison.OrdinalIgnoreCase)),
        "A missing action semicolon was not diagnosed.");
}

static void LifecycleActionListEditsPreserveTrivia()
{
    const string source = "INIT {\r\n    // first note\r\n    First(1);\r\n    // second note\r\n    Second(2);\r\n}\r\n";
    var document = LifecycleActionListDocument.Parse(source);

    Assert(document.Actions.SequenceEqual(["First(1)", "Second(2)"]),
        "The action list was not initialized in source order.");
    Assert(document.Move(1, -1), "Moving an action up should succeed.");
    document.Update(0, "Second(3)");
    document.Add("Third(4)");
    document.Remove(1);

    string result = document.ToSource();
    Assert(result.Contains("Second(3);", StringComparison.Ordinal) &&
        result.Contains("Third(4);", StringComparison.Ordinal),
        "Add, update or reorder did not produce the expected action sequence.");
    Assert(!result.Contains("First(1);", StringComparison.Ordinal),
        "The removed action remains in the generated source.");
    Assert(result.Contains("// first note", StringComparison.Ordinal) &&
        result.Contains("// second note", StringComparison.Ordinal),
        "Comments were lost while actions were reordered or removed.");
    Assert(result.Contains("\r\n", StringComparison.Ordinal),
        "CRLF line endings were not preserved.");

    var reparsed = new ActionListParser().Parse(result);
    Assert(reparsed.Diagnostics.Count == 0 && reparsed.Statements.Count == 2,
        "The edited lifecycle section did not parse after serialization.");
    Assert(reparsed.Statements.Select(statement => statement.Name)
        .SequenceEqual(["Second", "Third"]),
        "The final action order does not match the editor's order.");

    bool rejected = false;
    try { document.Add("Broken("); }
    catch (ArgumentException) { rejected = true; }
    Assert(rejected, "An invalid action was accepted by the document model.");
}

static void GlobalActionSectionIsNotSilentlyDiscarded()
{
    var dump = new StoryReteDump
    {
        FilePath = "synthetic",
        FileLength = 0,
        HeaderText = "",
        FormatVersion = [],
        CompilerVersion = "test",
        DebugFlags = 0,
        DivObjects = [],
        Functions = [],
        Nodes = [],
        Adaptors = [],
        DBases = [],
        Goals = [],
        GlobalActions = [new StoryActionRecord(0, "NOP", 0, [], 0, 0)],
        FinalPosition = 0,
        RemainingBytes = 0
    };

    StoryDivDecompileResult result = StoryDivDecompiler.Decompile(dump);
    Assert(result.Diagnostics.Any(d => d.Contains("1 global action(s)", StringComparison.Ordinal)),
        "A non-empty global action section was silently discarded.");
    Assert(!result.Text.Contains("NOP", StringComparison.Ordinal),
        "An unverified global action was emitted as source.");
}

static void RuleParserReportsMissingThen()
{
    const string source = """
        Goal(3) {
            KB {
                IF StringTable("x", 1)
            }
        }
        """;
    var (tokens, structure) = ParseStructure(source);
    var result = new RuleParser().Parse(source, tokens, structure.Blocks);
    Assert(result.Diagnostics.Any(d => d.Message.Contains("missing THEN", StringComparison.OrdinalIgnoreCase)),
        "Missing THEN was not diagnosed.");
}

static void ParserSpansStayInsideSource()
{
    const string source = """
        Goal(4) {
            KB {
                IF StringTable("x", 1) THEN NOP(1);
            }
        }
        """;
    var (tokens, structure) = ParseStructure(source);
    var result = new RuleParser().Parse(source, tokens, structure.Blocks);
    foreach (var rule in result.Rules)
    {
        Assert(rule.Start >= 0 && rule.Length > 0 && rule.Start + rule.Length <= source.Length,
            "Rule span is outside the source.");
        foreach (var condition in rule.Conditions)
            Assert(condition.Start >= 0 && condition.Start + condition.Length <= source.Length,
                "Condition span is outside the source.");
        foreach (var action in rule.Actions)
            Assert(action.Start >= 0 && action.Start + action.Length <= source.Length,
                "Action span is outside the source.");
    }
}

static void GrammarValidatorAcceptsConfirmedSyntax()
{
    const string source = """
        Goal(1).Title("Grammar test");
        Goal(1) {
            INIT {
                StartStoryFact(1);
            }
            KB {
                IF
                    IsReady(_Var1)
                    AND NOT
                    _Var1 != -10
                THEN
                    DoThing(_Var1);

                PROC
                    UpdateThing((INTEGER)_Var1, DIALOG_Test)
                THEN
                    GoalCompleted;
            }
            EXIT {
                StopStoryFact();
            }
        }
        """;

    var result = new OsirisGrammarValidator().Validate(source);
    Assert(result.ErrorCount == 0,
        "Confirmed syntax was rejected: " +
        string.Join(" | ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    Assert(result.RuleCount == 2, "Expected one IF and one PROC rule.");
    Assert(result.ConditionCount == 2, "Expected a predicate and a comparison condition.");
    Assert(result.ActionCount == 2, "Expected two KB actions.");
    Assert(result.LifecycleStatementCount == 2, "INIT and EXIT actions were not validated.");
}

static void GrammarValidatorDetectsMalformedSyntax()
{
    const string source = """
        Goal(1) {
            KB {
                IF BrokenCall(_Var1,, 2)
                AND
                AND
                _Var1 >
                THEN
                DoThing();
            }
        }
        /* unclosed comment
        """;

    var result = new OsirisGrammarValidator().Validate(source);
    Assert(result.Diagnostics.Any(d => d.Code == "LEX001"),
        "An unclosed block comment was not diagnosed.");
    Assert(result.Diagnostics.Any(d => d.Code == "KB013"),
        "An empty predicate argument was not diagnosed.");
    Assert(result.Diagnostics.Any(d => d.Code == "KB001" &&
        d.Message.Contains("Empty condition", StringComparison.OrdinalIgnoreCase)),
        "An empty condition between AND connectors was not diagnosed.");
    Assert(result.Diagnostics.Any(d => d.Code == "KB012"),
        "A comparison with a missing right-hand value was not diagnosed.");
}

static void GrammarValidatorRecoversAfterMissingConditionParenthesis()
{
    const string source = """
        KB {
        IF
        NewHour(ENGINE_GAME, 0
        AND
        Time(_Var1, _, _)
        /*
        sdfgdsdfg
        */
        And
        Time(_Var1, _, _)// test1
        AND
        IntegerSum(FUNCTION_Math, _Var1, 1, _Var4)
        THEN
        UpdateTime(_Var4, 0);
        }
        """;

    var result = new OsirisGrammarValidator().Validate(source);
    Assert(result.Diagnostics.Any(d => d.Code == "DEL002"),
        "The missing closing parenthesis was not diagnosed.");
    Assert(result.Diagnostics.Count <= 5,
        $"A single missing parenthesis caused {result.Diagnostics.Count} diagnostics: " +
        string.Join(" | ", result.Diagnostics.Take(10).Select(d => $"{d.Code}: {d.Message}")));
    Assert(result.Diagnostics.All(d => d.Code != "KB030"),
        "Parser recovery generated cascading 'outside an IF/PROC rule' errors.");

    var (tokens, structure) = ParseStructure(source);
    var parsed = new RuleParser().Parse(source, tokens, structure.Blocks);
    var recoveredRule = parsed.Rules.Single();
    Assert(recoveredRule.Conditions.Count == 4,
        $"Expected all 4 conditions to survive recovery, found {recoveredRule.Conditions.Count}.");
    Assert(recoveredRule.Conditions[1].Connector == RuleConditionConnector.And &&
        recoveredRule.Conditions[2].Connector == RuleConditionConnector.And,
        "AND connectors after the malformed call were not recovered.");
}

static void FullStoryPassesGrammarValidation(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException("STORYEDITOR_TEST_SOURCE does not exist.", path);

    string source = File.ReadAllText(path);
    var result = new OsirisGrammarValidator().Validate(source);
    Assert(result.ErrorCount == 0,
        $"The full Story produced {result.ErrorCount} grammar error(s): " +
        string.Join(" | ", result.Diagnostics.Take(20).Select(d => $"{d.Code} @{d.Position}: {d.Message}")));
    Assert(result.RuleCount == 4_918,
        $"Expected 4,918 KB rules, found {result.RuleCount}.");
    Assert(result.LifecycleStatementCount == 5_882,
        $"Expected 5,882 INIT/EXIT statements, found {result.LifecycleStatementCount}.");
}

static void BinaryStoryDecompiles(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException("STORYEDITOR_TEST_STORY does not exist.", path);

    StoryReteDump dump = StoryReteExporter.Read(path);
    StoryDivDecompileResult result = StoryDivDecompiler.Decompile(dump);
    int actionNodeCount = dump.Nodes.Count(node => node.NodeType == 7);

    Assert(dump.RemainingBytes == 0, $"Binary parser left {dump.RemainingBytes} unread byte(s).");
    Assert(result.UnassignedRuleCount == 0, $"{result.UnassignedRuleCount} action nodes were not assigned to Goals.");
    Assert(result.RuleCount == actionNodeCount,
        $"Decompiler emitted {result.RuleCount} rules for {actionNodeCount} action nodes.");
    Assert(result.GoalCount == dump.Goals.Count, "Goal count changed during decompilation.");

    var emittedTokens = Lexer.Tokenize(result.Text);
    var emittedStructure = new StructureParser().Parse(result.Text, emittedTokens);
    Assert(emittedStructure.Diagnostics.Count == 0, "Emitted source has structural diagnostics.");
    var emittedCatalog = GoalCatalog.Build(emittedStructure.Blocks);
    var emittedApi = ApiSymbolCatalog.Build(result.Text);
    var actionParser = new ActionListParser();
    int lifecycleCallCount = 0;
    foreach (var goal in emittedCatalog.Goals)
    {
        foreach (var sectionKind in new[] { SyntaxKind.InitSection, SyntaxKind.ExitSection })
        {
            var section = goal.Block.Children.FirstOrDefault(block => block.Kind == sectionKind);
            if (section is null) continue;
            var parsedActions = actionParser.Parse(result.Text.Substring(section.Start, section.Length), emittedApi);
            Assert(parsedActions.Diagnostics.Count == 0,
                $"Goal {goal.Id} {sectionKind} failed lifecycle validation: " +
                string.Join(" | ", parsedActions.Diagnostics.Select(d => d.Message)));
            lifecycleCallCount += parsedActions.Calls.Count;
        }
    }
    Assert(result.Diagnostics.Any(d => d.Contains("zero-padded", StringComparison.Ordinal)) == StoryDivDecompiler.HasLikelySavePaddedAdaptorMappings(dump.Adaptors),
        "Saved-story adaptor padding warning did not match the binary layout.");
    Assert(!result.Diagnostics.Any(d => d.Contains("has no complete typed prototype", StringComparison.Ordinal)), "A PROC typed prototype could not be inferred.");
    Assert(result.Diagnostics.Any(d => d.Contains("global action(s)", StringComparison.Ordinal)) == (dump.GlobalActions.Count > 0),
        "Global-action omission diagnostic did not match the binary section.");

    int initActionCount = dump.Goals.Sum(goal => goal.InitActions.Count);
    int exitActionCount = dump.Goals.Sum(goal => goal.ExitActions.Count);
    StoryGoalRecord[] orderedGoals = dump.Goals.OrderBy(goal => goal.Metadata).ToArray();
    for (int i = 0; i < orderedGoals.Length; i++)
    {
        StoryGoalRecord goal = orderedGoals[i];
        string marker = $"Goal({goal.Metadata}).Title(";
        int goalStart = result.Text.IndexOf(marker, StringComparison.Ordinal);
        Assert(goalStart >= 0, $"Goal {goal.Metadata} was not emitted.");

        int goalEnd = i + 1 < orderedGoals.Length
            ? result.Text.IndexOf($"Goal({orderedGoals[i + 1].Metadata}).Title(", goalStart + marker.Length, StringComparison.Ordinal)
            : result.Text.IndexOf($"Goal({orderedGoals[0].Metadata}).SubGoals(", goalStart, StringComparison.Ordinal);
        if (goalEnd < 0)
            goalEnd = result.Text.Length;

        string goalText = result.Text.Substring(goalStart, goalEnd - goalStart);
        string[] actualInit = ExtractActionSection(goalText, "INIT", "KB");
        string[] actualExit = ExtractActionSection(goalText, "EXIT", null);
        string[] expectedInit = StoryDivDecompiler.RenderActionList(goal.InitActions).ToArray();
        string[] expectedExit = StoryDivDecompiler.RenderActionList(goal.ExitActions).ToArray();

        Assert(actualInit.SequenceEqual(expectedInit),
            $"Goal {goal.Metadata} INIT action list changed during decompilation.");
        Assert(actualExit.SequenceEqual(expectedExit),
            $"Goal {goal.Metadata} EXIT action list changed during decompilation.");
    }

    Console.WriteLine($"      Binary: {Path.GetFileName(path)}; Goals={result.GoalCount}; Rules={result.RuleCount}; INIT={initActionCount}; EXIT={exitActionCount}; Parsed lifecycle calls={lifecycleCallCount}; Warnings={result.Diagnostics.Count}");
}

static string[] ExtractActionSection(string goalText, string section, string? nextSection)
{
    string marker = section + " {";
    int contentStart = goalText.IndexOf(marker, StringComparison.Ordinal);
    Assert(contentStart >= 0, $"Missing {section} section in emitted Goal.");
    contentStart += marker.Length;

    string endMarker = nextSection is null
        ? Environment.NewLine + "}"
        : Environment.NewLine + "}" + Environment.NewLine + nextSection + " {";
    int contentEnd = goalText.IndexOf(endMarker, contentStart, StringComparison.Ordinal);
    Assert(contentEnd >= 0, $"Could not find end of {section} section.");

    return goalText.Substring(contentStart, contentEnd - contentStart)
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToArray();
}

static (IReadOnlyList<Token> Tokens, StructureParseResult Structure) ParseStructure(string source)
{
    var tokens = Lexer.Tokenize(source);
    var structure = new StructureParser().Parse(source, tokens);
    Assert(structure.Diagnostics.Count == 0, "Structure parser reported diagnostics.");
    return (tokens, structure);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

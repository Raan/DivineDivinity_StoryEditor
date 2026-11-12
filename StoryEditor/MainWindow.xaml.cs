using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ICSharpCode.AvalonEdit;
using StoryEditor2.Lexing;
using StoryEditor2.Parsing;
using StoryCompiler;

namespace StoryEditor2.Wpf;

public partial class MainWindow : Window
{
    private sealed record EditedSection(int OriginalLength, string Text);
    private sealed record RuleListEntry(string Label, int Start, int Length, OsirisRule Rule, string SearchText);
    private sealed record CrossReferenceEntry(string Label, int Start, int Length, GoalSymbol Goal, string SearchText, IReadOnlyList<string> Calls, IReadOnlyList<string> ProcedureCalls, string? ProcedureHeadName);
    private sealed record GoalLinkEntry(
        GoalSymbol Goal,
        int CurrentGoalId,
        IReadOnlyList<GoalDependencyReference> Dependencies,
        IReadOnlyList<string> SharedCalls,
        string Label)
    {
        public int ConfirmedCount => Dependencies.Count(d =>
            d.Confidence == GoalDependencyConfidence.ApiDeclared);
        public int ProcedureCount => Dependencies.Count(d =>
            d.Confidence == GoalDependencyConfidence.StoryProcedureHandler);
        public int OutgoingProcedureCount => Dependencies.Count(d =>
            d.SourceGoal.Id == CurrentGoalId &&
            d.Confidence == GoalDependencyConfidence.StoryProcedureHandler);
        public int IncomingProcedureCount => Dependencies.Count(d =>
            d.TargetGoal?.Id == CurrentGoalId &&
            d.Confidence == GoalDependencyConfidence.StoryProcedureHandler);
        public int CandidateCount => Dependencies.Count(d =>
            d.Confidence == GoalDependencyConfidence.NameBasedCandidate);
        public int OutgoingCount => Dependencies.Count(d =>
            d.SourceGoal.Id == CurrentGoalId);
        public int IncomingCount => Dependencies.Count(d =>
            d.TargetGoal?.Id == CurrentGoalId);
        public int OutgoingConfirmedCount => Dependencies.Count(d =>
            d.SourceGoal.Id == CurrentGoalId &&
            d.Confidence == GoalDependencyConfidence.ApiDeclared);
        public int IncomingConfirmedCount => Dependencies.Count(d =>
            d.TargetGoal?.Id == CurrentGoalId &&
            d.Confidence == GoalDependencyConfidence.ApiDeclared);
    }
    private sealed record GoalDependencyEvidenceEntry(
        GoalDependencyReference? Dependency,
        string Label);
    private sealed record SymbolExplorerEntry(string Name, string Kind, string Signature,
        int UsageCount, string Label, string DefinitionSummary = "", int? DefinitionStart = null, int? DefinitionLength = null, int? DefinitionGoalId = null);
    private readonly List<SymbolExplorerEntry> _symbolEntries = new();
    private readonly List<GoalLinkEntry> _goalLinkEntries = new();
    private readonly List<GoalDependencyEvidenceEntry> _goalDependencyEvidenceEntries = new();
    private GoalDependencyAnalysisResult? _goalDependencyAnalysis;
    private readonly Dictionary<int, string> _goalDependencyKbSnapshots = new();
    private string? _goalDependencyAnalysisSource;

    private readonly Dictionary<int, EditedSection> _edits = new();
    private readonly List<RuleListEntry> _allRuleEntries = new();
    private readonly List<CrossReferenceEntry> _crossReferenceEntries = new();
    private static readonly HashSet<string> KnownOsirisBuiltins = new(StringComparer.OrdinalIgnoreCase)
    {
        "GoalCompleted", "SetStatus", "MergeStatus"
    };
    private GoalSymbol? _currentGoal;
    private readonly OsirisColorizer _kbColorizer = new();
    private readonly OsirisColorizer _initColorizer = new();
    private readonly OsirisColorizer _exitColorizer = new();
    private readonly DispatcherTimer _highlightTimer;
    private string? _source;
    private string? _sourcePath;
    private IReadOnlyList<Token> _tokens = Array.Empty<Token>();
    private StructureParseResult? _structure;
    private GoalCatalog? _catalog;
    private GoalHierarchyResult? _hierarchy;
    private ApiSymbolCatalog? _api;
    private int _kbStart;
    private int _kbOriginalLength;
    private bool _loadingSection;
    private bool _hasStructuralEdits;

    public MainWindow(string? initialPath)
    {
        InitializeComponent();

        KbEditor.TextArea.TextView.LineTransformers.Add(_kbColorizer);
        InitEditor.TextArea.TextView.LineTransformers.Add(_initColorizer);
        ExitEditor.TextArea.TextView.LineTransformers.Add(_exitColorizer);

        _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            UpdateColorizer(KbEditor, _kbColorizer);
            RebuildRuleList();
            UpdateCrossReferencesForCurrentGoal();
        };

        KbEditor.TextChanged += (_, _) =>
        {
            if (_loadingSection || _source is null || _kbOriginalLength == 0) return;
            string original = _source.Substring(_kbStart, _kbOriginalLength);
            if (string.Equals(KbEditor.Text, original, StringComparison.Ordinal))
                _edits.Remove(_kbStart);
            else
                _edits[_kbStart] = new EditedSection(_kbOriginalLength, KbEditor.Text);

            SaveButton.IsEnabled = _edits.Count > 0 || _hasStructuralEdits;
            StatusText.Text = _edits.Count == 0 && !_hasStructuralEdits ? "No unsaved changes" :
                $"Unsaved KB edits: {_edits.Count} Goal section(s)";
            _highlightTimer.Stop();
            _highlightTimer.Start();
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            LoadDocument(initialPath);
    }

    private void ConfigureGame_Click(object sender, RoutedEventArgs e)
    {
        var installation = GameInstallationSettings.PromptForInstallation(this);
        if (installation is null) return;

        StatusText.Text = "Game installation configured";
        MessageBox.Show(this,
            "Game installation saved for future builds.\n\n" +
            "OsirisDLL: " + installation.OsirisDllPath + "\n" +
            "Base Story: " + installation.BaseStoryPath + "\n\n" +
            "The editor will use these files as read-only build inputs.",
            "Game installation configured", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void DecompileStory_Click(object sender, RoutedEventArgs e)
    {
        if (SaveButton.IsEnabled &&
            MessageBox.Show(this,
                "The current document has unsaved edits. Decompiling a new Story will discard them. Continue?",
                "Unsaved edits", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var installation = GameInstallationSettings.Load();
        string initialDirectory = installation is not null && File.Exists(installation.BaseStoryPath)
            ? Path.GetDirectoryName(installation.BaseStoryPath)!
            : Environment.CurrentDirectory;

        var inputDialog = new OpenFileDialog
        {
            Title = "Select a Divine Divinity Story binary",
            Filter = "Osiris Story binaries (*.000;*.001)|*.000;*.001|All files (*.*)|*.*",
            InitialDirectory = initialDirectory,
            CheckFileExists = true
        };
        if (inputDialog.ShowDialog(this) != true) return;

        string inputPath = Path.GetFullPath(inputDialog.FileName);
        var outputDialog = new SaveFileDialog
        {
            Title = "Save decompiled Osiris source",
            Filter = "Osiris source (*.div)|*.div|All files (*.*)|*.*",
            DefaultExt = "div",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(inputPath) + "_decompiled.div",
            InitialDirectory = Path.GetDirectoryName(inputPath),
            OverwritePrompt = true
        };
        if (outputDialog.ShowDialog(this) != true) return;

        string outputPath = Path.GetFullPath(outputDialog.FileName);
        DecompileButton.IsEnabled = false;
        StatusText.Text = "Decompiling Story binary...";
        OutputBox.Text = "Reading the binary and reconstructing Osiris source. The original file is read-only.";

        try
        {
            var completed = await Task.Run(() =>
            {
                StoryReteDump dump = StoryReteExporter.Read(inputPath);
                StoryDivDecompileResult result = StoryDivDecompiler.Decompile(dump);
                return (Dump: dump, Result: result);
            });

            if (completed.Dump.RemainingBytes != 0 ||
                completed.Result.UnassignedRuleCount != 0 ||
                completed.Result.Diagnostics.Count != 0)
            {
                string diagnostics = string.Join(Environment.NewLine, completed.Result.Diagnostics);
                throw new InvalidDataException(
                    $"The decompiler could not verify a complete reconstruction.\n" +
                    $"Unread bytes: {completed.Dump.RemainingBytes}\n" +
                    $"Unassigned rules: {completed.Result.UnassignedRuleCount}\n" +
                    $"Diagnostics: {completed.Result.Diagnostics.Count}\n{diagnostics}");
            }

            File.WriteAllText(outputPath, completed.Result.Text, new UTF8Encoding(false));
            LoadDocument(outputPath);
            OutputBox.Text +=
                "\r\n\r\nDECOMPILATION SUCCEEDED\r\n" +
                $"Objects: {completed.Dump.DivObjects.Count:N0}\r\n" +
                $"API declarations: {completed.Dump.Functions.Count(x => x.FunctionKind is 1 or 2 or 3 or 6 or 7):N0}\r\n" +
                $"Goals: {completed.Result.GoalCount:N0}\r\n" +
                $"Rules: {completed.Result.RuleCount:N0}\r\n" +
                $"Inferred PROC prototypes: {completed.Result.InferredPrototypeCount:N0}\r\n" +
                $"Inferred PROC parameters: {completed.Result.InferredParameterCount:N0}\r\n" +
                "Original Story binary was not modified.";
            StatusText.Text = $"Decompiled {completed.Result.GoalCount:N0} Goals / {completed.Result.RuleCount:N0} rules";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Decompilation failed";
            OutputBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "Story decompilation failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DecompileButton.IsEnabled = true;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Osiris source",
            Filter = "Osiris source (*.div)|*.div|All files (*.*)|*.*",
            InitialDirectory = !string.IsNullOrWhiteSpace(_sourcePath) &&
                Directory.Exists(Path.GetDirectoryName(_sourcePath))
                    ? Path.GetDirectoryName(_sourcePath)! : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) == true)
            LoadDocument(dialog.FileName);
    }

    private void LoadDocument(string path)
    {
        try
        {
            _sourcePath = path;
            _source = File.ReadAllText(path);
            _goalDependencyAnalysis = null;
            _goalDependencyAnalysisSource = null;
            _goalDependencyKbSnapshots.Clear();
            _tokens = Lexer.Tokenize(_source);
            _structure = new StructureParser().Parse(_source, _tokens);
            _catalog = GoalCatalog.Build(_structure.Blocks);
            _hierarchy = GoalHierarchy.Build(_source, _catalog);
            _api = ApiSymbolCatalog.Build(_source);
            _edits.Clear();
            _hasStructuralEdits = false;
            SaveButton.IsEnabled = false;
            BuildButton.IsEnabled = true;
            NewGoalButton.IsEnabled = true;

            DocumentLabel.Text = Path.GetFileName(path);
            OutputBox.Text =
                $"Loaded: {path}\r\n" +
                $"Characters: {_source.Length:N0}\r\n" +
                $"Tokens: {_tokens.Count - 1:N0}\r\n" +
                $"Goals: {_catalog.Goals.Count:N0}\r\n" +
                $"Hierarchy links: {_hierarchy.LinkCount:N0}\r\n" +
                $"Hierarchy diagnostics: {_hierarchy.Diagnostics.Count:N0}\r\n" +
                "WPF + AvalonEdit prototype. Source is never overwritten.";
            BuildTree();
            BuildCrossReferenceIndex();
            BuildSymbolExplorer();
            BuildGoalDependencyExplorer();
            StatusText.Text = $"Loaded {_catalog.Goals.Count:N0} Goals";
            if (GoalTree.Items.Count > 0 && GoalTree.Items[0] is TreeViewItem first)
                first.IsSelected = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Could not open source",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewGoal_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _catalog is null)
            return;

        try
        {
            // Fold pending KB edits into the working source before changing offsets.
            string baseSource = CreateUpdatedSource();
            var baseTokens = Lexer.Tokenize(baseSource);
            var baseStructure = new StructureParser().Parse(baseSource, baseTokens);
            var baseCatalog = GoalCatalog.Build(baseStructure.Blocks);
            var orderedGoals = baseCatalog.Goals.OrderBy(g => g.Id).ToArray();

            if (baseStructure.Diagnostics.Count > 0 ||
                orderedGoals.Length != _catalog.Goals.Count ||
                orderedGoals.Where((goal, index) => goal.Id != index + 1).Any())
            {
                MessageBox.Show(this,
                    "Cannot insert a Goal safely: the current source has structural diagnostics or non-contiguous Goal IDs.",
                    "Goal insertion blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new NewGoalWindow(title => GetGoalInsertionId(title, orderedGoals), orderedGoals) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            int? selectedParentId = dialog.ParentGoalId;
            string title = dialog.GoalTitle;
            if (title.Length == 0 || title.Contains('\r') || title.Contains('\n'))
            {
                MessageBox.Show(this, "Enter a non-empty, single-line title.", "Invalid Goal title",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (orderedGoals.Any(g => string.Equals(g.Title, title, StringComparison.Ordinal)))
            {
                MessageBox.Show(this, "A Goal with this exact title already exists.", "Duplicate Goal title",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int newId = GetGoalInsertionId(title, orderedGoals);
            string newline = baseSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");

            // Remap every syntactic Goal(n) reference, excluding strings and comments via lexer tokens.
            string remappedSource = RemapGoalReferences(baseSource, baseTokens, newId);
            var remappedTokens = Lexer.Tokenize(remappedSource);
            var remappedStructure = new StructureParser().Parse(remappedSource, remappedTokens);
            var remappedCatalog = GoalCatalog.Build(remappedStructure.Blocks);

            string goalBlock = string.Join(newline, new[]
            {
                $"Goal({newId}).Title(\"{escapedTitle}\");",
                $"Goal({newId}) {{",
                "INIT {",
                "}",
                "",
                "KB {",
                "}",
                "",
                "EXIT {",
                "}",
                "}"
            }) + newline + newline;

            int insertionOffset;
            string insertionText;
            if (newId <= orderedGoals.Length)
            {
                int shiftedTargetId = newId + 1;
                var titleMatch = Regex.Match(remappedSource,
                    $@"(?m)^[ \t]*Goal\s*\(\s*{shiftedTargetId}\s*\)\s*\.\s*Title\s*\(");
                if (!titleMatch.Success)
                    throw new InvalidOperationException($"Could not find the title declaration for shifted Goal {shiftedTargetId}.");

                insertionOffset = titleMatch.Index;
                insertionText = goalBlock;
            }
            else
            {
                var lastGoal = remappedCatalog.Goals.OrderBy(g => g.Id).LastOrDefault()
                    ?? throw new InvalidOperationException("The source contains no Goal block.");
                insertionOffset = lastGoal.Block.End;
                insertionText = newline + newline + goalBlock;
            }

            string candidate = remappedSource.Insert(insertionOffset, insertionText);
            var laterMode = Regex.Matches(candidate,
                    @"(?m)^[ \t]*Goal\s*\(\s*(\d+)\s*\)\s*\.\s*SubGoals\s*\(")
                .Cast<Match>()
                .FirstOrDefault(m => int.TryParse(m.Groups[1].Value, out int id) && id > newId);
            string modeDeclaration = $"Goal({newId}).SubGoals(OR);{newline}";
            if (laterMode is not null)
            {
                candidate = candidate.Insert(laterMode.Index, modeDeclaration);
            }
            else
            {
                var hierarchyDeclarations = Regex.Matches(candidate,
                    @"(?m)^[ \t]*Goal\s*\(\s*\d+\s*\)\s*\.\s*SubGoals?\s*\([^;\r\n]*\)\s*;[ \t]*(?:\r?\n|$)");
                var lastDeclaration = hierarchyDeclarations.Count > 0
                    ? hierarchyDeclarations[hierarchyDeclarations.Count - 1]
                    : null;
                int modeOffset = lastDeclaration is null
                    ? candidate.Length
                    : lastDeclaration.Index + lastDeclaration.Length;
                string prefix = modeOffset > 0 && candidate[modeOffset - 1] is not '\r' and not '\n'
                    ? newline : string.Empty;
                candidate = candidate.Insert(modeOffset, prefix + modeDeclaration);
            }

            if (selectedParentId is int parentSelection)
            {
                int remappedParentId = parentSelection >= newId ? parentSelection + 1 : parentSelection;
                candidate = AddParentGoalLink(candidate, remappedParentId, newId, newline);
            }

            var candidateTokens = Lexer.Tokenize(candidate);
            var candidateStructure = new StructureParser().Parse(candidate, candidateTokens);
            var candidateCatalog = GoalCatalog.Build(candidateStructure.Blocks);
            var candidateHierarchy = GoalHierarchy.Build(candidate, candidateCatalog);
            var candidateRules = new RuleParser().Parse(candidate, candidateTokens, candidateStructure.Blocks);

            bool idsAreContiguous = candidateCatalog.Goals.Count == orderedGoals.Length + 1 &&
                candidateCatalog.Goals.OrderBy(g => g.Id)
                    .Select((goal, index) => goal.Id == index + 1).All(valid => valid);
            bool oldGoalsPreserved = orderedGoals.All(oldGoal =>
            {
                int expectedId = oldGoal.Id >= newId ? oldGoal.Id + 1 : oldGoal.Id;
                return candidateCatalog.Goals.Any(g =>
                    g.Id == expectedId && string.Equals(g.Title, oldGoal.Title, StringComparison.Ordinal));
            });
            bool newGoalValid = candidateCatalog.Goals.Any(g =>
                g.Id == newId && string.Equals(g.Title, escapedTitle, StringComparison.Ordinal));
            int expectedParentId = selectedParentId.HasValue
                ? (selectedParentId.Value >= newId ? selectedParentId.Value + 1 : selectedParentId.Value)
                : 0;
            bool parentLinkValid = !selectedParentId.HasValue || Regex.IsMatch(candidate,
                $@"Goal\s*\(\s*{expectedParentId}\s*\)\s*\.\s*SubGoal\s*\(\s*{newId}\s*\)\s*;");
            bool hierarchyPreserved = candidateHierarchy.LinkCount == (_hierarchy?.LinkCount ?? 0) + (selectedParentId.HasValue ? 1 : 0) &&
                candidateHierarchy.Diagnostics.Count == (_hierarchy?.Diagnostics.Count ?? 0);

            if (!idsAreContiguous || !oldGoalsPreserved || !newGoalValid ||
                candidateStructure.Diagnostics.Count > 0 ||
                candidateRules.Diagnostics.Count > 0 || !hierarchyPreserved || !parentLinkValid)
            {
                string details = string.Join(Environment.NewLine,
                    candidateStructure.Diagnostics.Select(d => d.Message)
                    .Concat(candidateRules.Diagnostics.Select(d => d.Message))
                    .Concat(candidateHierarchy.Diagnostics)
                    .Take(30));
                MessageBox.Show(this,
                    "The Goal insertion was rejected by validation." + Environment.NewLine + Environment.NewLine +
                    $"IDs contiguous: {idsAreContiguous}" + Environment.NewLine +
                    $"Existing Goals preserved: {oldGoalsPreserved}" + Environment.NewLine +
                    $"Hierarchy preserved: {hierarchyPreserved}" + Environment.NewLine +
                    $"Parent link valid: {parentLinkValid}" + Environment.NewLine + details,
                    "Goal validation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _source = candidate;
            _tokens = candidateTokens;
            _structure = candidateStructure;
            _catalog = candidateCatalog;
            _hierarchy = candidateHierarchy;
            _edits.Clear(); // Pending KB edits are now incorporated into _source.
            _currentGoal = null;
            _hasStructuralEdits = true;
            SaveButton.IsEnabled = true;
            BuildButton.IsEnabled = true;

            BuildTree();
            BuildCrossReferenceIndex();
            BuildSymbolExplorer();
            _goalDependencyAnalysis = null;
            _goalDependencyAnalysisSource = null;
            BuildGoalDependencyExplorer();

            var item = FindGoalTreeItem(newId);
            if (item is not null)
                item.IsSelected = true;

            StatusText.Text = selectedParentId.HasValue
                ? $"Inserted Goal {newId} — {title} under Goal {expectedParentId}; IDs and references remapped"
                : $"Inserted Goal {newId} — {title} alphabetically; all later IDs and references were remapped";
            OutputBox.Text =
                $"Inserted Goal {newId} — {title}" + Environment.NewLine +
                $"Previous Goals: {orderedGoals.Length}" + Environment.NewLine +
                $"New Goal count: {candidateCatalog.Goals.Count}" + Environment.NewLine +
                (selectedParentId.HasValue ? $"Parent Goal: {expectedParentId}" + Environment.NewLine : string.Empty) +
                $"Hierarchy links preserved: {candidateHierarchy.LinkCount}" + Environment.NewLine +
                "Existing Goal IDs and all Goal(n) references were remapped." + Environment.NewLine +
                "Structural parser: passed" + Environment.NewLine +
                "Rule parser: passed" + Environment.NewLine +
                "Hierarchy validation: passed" + Environment.NewLine +
                "Pending KB edits were retained in the working source." + Environment.NewLine +
                "The source file on disk has not been modified.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Could not create Goal",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static int GetGoalInsertionId(string title, IReadOnlyList<GoalSymbol> orderedGoals)
    {
        foreach (var goal in orderedGoals.OrderBy(g => g.Id))
            if (title.CompareTo(goal.Title ?? string.Empty) < 0)
                return goal.Id;
        return orderedGoals.Count + 1;
    }

    private static string AddParentGoalLink(string source, int parentId, int childId, string newline)
    {
        var declarations = Regex.Matches(source,
            @"(?m)^[ \t]*Goal\s*\(\s*(\d+)\s*\)\s*\.\s*SubGoals?\s*\([^;\r\n]*\)\s*;[ \t]*(?:\r?\n|$)");
        bool hasMode = Regex.IsMatch(source,
            $@"(?m)^[ \t]*Goal\s*\(\s*{parentId}\s*\)\s*\.\s*SubGoals\s*\(\s*(?:AND|OR)\s*\)\s*;");
        if (!hasMode)
        {
            Match? next = declarations.Cast<Match>().FirstOrDefault(m =>
                int.TryParse(m.Groups[1].Value, out int id) && id > parentId);
            int offset = next?.Index ?? (declarations.Count > 0
                ? declarations[declarations.Count - 1].Index + declarations[declarations.Count - 1].Length
                : source.Length);
            string prefix = offset > 0 && source[offset - 1] is not '\r' and not '\n' ? newline : string.Empty;
            source = source.Insert(offset, prefix + string.Format("Goal({0}).SubGoals(OR);{1}", parentId, newline));
        }
        var childLinks = Regex.Matches(source,
            @"(?m)^[ \t]*Goal\s*\(\s*(\d+)\s*\)\s*\.\s*SubGoal\s*\(\s*\d+\s*\)\s*;[ \t]*(?:\r?\n|$)");
        var sameParent = childLinks.Cast<Match>()
            .Where(m => int.TryParse(m.Groups[1].Value, out int id) && id == parentId).ToArray();
        int linkOffset;
        if (sameParent.Length > 0)
        {
            var last = sameParent[^1];
            linkOffset = last.Index + last.Length;
        }
        else
        {
            var nextParent = childLinks.Cast<Match>().FirstOrDefault(m =>
                int.TryParse(m.Groups[1].Value, out int id) && id > parentId);
            linkOffset = nextParent?.Index ?? (childLinks.Count > 0
                ? childLinks[childLinks.Count - 1].Index + childLinks[childLinks.Count - 1].Length
                : source.Length);
        }
        string linkPrefix = linkOffset > 0 && source[linkOffset - 1] is not '\r' and not '\n' ? newline : string.Empty;
        return source.Insert(linkOffset, linkPrefix + string.Format("Goal({0}).SubGoal({1});{2}", parentId, childId, newline));
    }

    private static string RemapGoalReferences(string source, IReadOnlyList<Token> tokens, int firstIdToShift)
    {
        var significant = tokens.Where(t => t.Kind != TokenKind.Trivia &&
                                             t.Kind != TokenKind.EndOfFile).ToArray();
        var replacements = new List<(int Start, int Length, string Value)>();

        for (int i = 0; i + 3 < significant.Length; i++)
        {
            if (significant[i].Kind != TokenKind.Identifier ||
                !(string.Equals(significant[i].Text, "Goal", StringComparison.Ordinal) ||
                  string.Equals(significant[i].Text, "SubGoal", StringComparison.Ordinal)) ||
                significant[i + 1].Text != "(" ||
                significant[i + 2].Kind != TokenKind.IntegerLiteral ||
                significant[i + 3].Text != ")")
                continue;

            if (int.TryParse(significant[i + 2].Text, out int oldId) && oldId >= firstIdToShift)
                replacements.Add((significant[i + 2].Start, significant[i + 2].Length,
                    (oldId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        string updated = source;
        foreach (var replacement in replacements.OrderByDescending(r => r.Start))
            updated = updated.Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, replacement.Value);
        return updated;
    }
    private void BuildTree()
    {
        GoalTree.Items.Clear();
        if (_hierarchy is null) return;
        string filter = SearchBox.Text.Trim();
        foreach (var root in _hierarchy.Roots)
        {
            var item = CreateTreeItem(root, filter);
            if (item is not null) GoalTree.Items.Add(item);
        }
    }

    private TreeViewItem? CreateTreeItem(GoalTreeNode node, string filter)
    {
        var children = node.Children.Select(child => CreateTreeItem(child, filter))
            .Where(item => item is not null).Cast<TreeViewItem>().ToArray();
        string title = node.Goal.Title ?? "(untitled)";
        bool ownMatch = filter.Length == 0 ||
            node.Goal.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            title.Contains(filter, StringComparison.OrdinalIgnoreCase);
        if (!ownMatch && children.Length == 0) return null;

        string suffix = node.Children.Count > 0 && node.ChildMode is not null
            ? $" [{node.ChildMode}]" : "";
        var item = new TreeViewItem
        {
            Header = $"{node.Goal.Id} {title}{suffix}",
            Tag = node.Goal,
            IsExpanded = filter.Length > 0
        };
        foreach (var child in children) item.Items.Add(child);
        return item;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_hierarchy is not null) BuildTree();
    }

    private void GoalTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: GoalSymbol goal } selectedItem)
            return;

        ShowGoal(goal);
        BringGoalTreeItemIntoView(selectedItem);
    }

    private void BringGoalTreeItemIntoView(TreeViewItem? item)
    {
        if (item is null)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!item.IsSelected)
                return;

            GoalTree.UpdateLayout();
            item.BringIntoView();
            GoalTree.UpdateLayout();
        }));
    }

    private void ShowGoal(GoalSymbol goal)
    {
        _currentGoal = goal;
        if (_source is null) return;
        var init = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.InitSection);
        var kb = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.KnowledgeBaseSection);
        var exit = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.ExitSection);

        _loadingSection = true;
        InitEditor.Text = Slice(init);
        _kbStart = kb?.Start ?? 0;
        _kbOriginalLength = kb?.Length ?? 0;
        KbEditor.Text = kb is null ? "" :
            (_edits.TryGetValue(kb.Start, out var edit)
                ? edit.Text : _source.Substring(kb.Start, kb.Length));
        ExitEditor.Text = Slice(exit);
        _loadingSection = false;

        UpdateColorizer(InitEditor, _initColorizer);
        UpdateColorizer(KbEditor, _kbColorizer);
        UpdateColorizer(ExitEditor, _exitColorizer);
        RebuildRuleList();
        BuildGoalDependencyExplorer();
        StatusText.Text = $"Goal {goal.Id} — {goal.Title}";
        BringGoalTreeItemIntoView(FindGoalTreeItem(goal.Id));
    }

    private void RebuildRuleList()
    {
        _allRuleEntries.Clear();
        RuleList.Items.Clear();
        if (string.IsNullOrWhiteSpace(KbEditor.Text))
        {
            OutputBox.Text =
                $"Goal {(_currentGoal?.Id.ToString() ?? "—")} — {_currentGoal?.Title ?? "(no Goal selected)"}" +
                Environment.NewLine + "No KB rules or content.";
            return;
        }

        try
        {
            // Parse the currently displayed KB text, not the original file,
            // so the rule navigator follows unsaved edits as well.
            var tokens = Lexer.Tokenize(KbEditor.Text);
            var structure = new StructureParser().Parse(KbEditor.Text, tokens);
            var parsed = new RuleParser().Parse(KbEditor.Text, tokens, structure.Blocks);

            int index = 0;
            foreach (var rule in parsed.Rules)
            {
                index++;
                string detail = rule.Kind == RuleKind.If
                    ? rule.Conditions.FirstOrDefault()?.Text ?? "no conditions"
                    : rule.ProcedureSignature ?? "procedure";
                detail = CompactRuleText(detail, 58);
                string action = rule.Actions.FirstOrDefault()?.Text ?? "";
                string summary = rule.Kind == RuleKind.If
                    ? $"IF  {detail}"
                    : $"PROC  {detail}";
                if (rule.Kind == RuleKind.If && action.Length > 0)
                    summary += $"  →  {CompactRuleText(action, 32)}";

                string raw = KbEditor.Text.Substring(rule.Start, rule.Length);
                string searchable = raw + " " + string.Join(" ", rule.Calls.Select(c => c.Name));
                _allRuleEntries.Add(new RuleListEntry(
                    $"{index:D3}  {summary}", rule.Start, rule.Length, rule, searchable));
            }

            int ifCount = parsed.Rules.Count(r => r.Kind == RuleKind.If);
            int procCount = parsed.Rules.Count - ifCount;
            var callsByName = parsed.Rules.SelectMany(r => r.Calls)
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int declaredCalls = _api is null ? 0 : callsByName
                .Where(g => _api.Functions.ContainsKey(g.Key)).Sum(g => g.Count());
            var unresolvedCalls = callsByName
                .Where(g => (_api is null || !_api.Functions.ContainsKey(g.Key)) &&
                            !KnownOsirisBuiltins.Contains(g.Key))
                .ToArray();
            RuleList.ToolTip = parsed.Diagnostics.Count == 0
                ? $"{parsed.Rules.Count:N0} rules"
                : $"{parsed.Rules.Count:N0} rules; {parsed.Diagnostics.Count:N0} parser diagnostics";

            string newline = Environment.NewLine;
            int unresolvedOccurrenceCount = unresolvedCalls.Sum(g => g.Count());
            OutputBox.Text =
                $"Goal {(_currentGoal?.Id.ToString() ?? "—")} — {_currentGoal?.Title ?? "(no Goal selected)"}{newline}" +
                $"Rules: {parsed.Rules.Count:N0} (IF: {ifCount:N0}, PROC: {procCount:N0}){newline}" +
                $"Conditions: {parsed.Rules.Sum(r => r.Conditions.Count):N0}  |  " +
                $"Actions: {parsed.Rules.Sum(r => r.Actions.Count):N0}{newline}" +
                $"Function calls: {parsed.FunctionCallCount:N0}  |  " +
                $"Declared API matches: {declaredCalls:N0}{newline}" +
                $"Parser diagnostics: {parsed.Diagnostics.Count:N0}";

            if (unresolvedCalls.Length > 0)
            {
                OutputBox.Text += newline + newline +
                    $"Unresolved call names: {unresolvedCalls.Length:N0} " +
                    $"({unresolvedOccurrenceCount:N0} occurrences). " +
                    "These are review hints, not parser errors. " +
                    "See Symbol Explorer → Unresolved calls for details.";
            }

            if (parsed.Diagnostics.Count > 0)
            {
                OutputBox.Text += newline + newline + "Parser diagnostics:" + newline +
                    string.Join(newline, parsed.Diagnostics.Take(20)
                        .Select(d => $"  Position {d.Position}: {d.Message}"));
                if (parsed.Diagnostics.Count > 20)
                    OutputBox.Text += newline +
                        $"  ... and {parsed.Diagnostics.Count - 20:N0} more.";
            }
            FilterRuleList();
        }
        catch (Exception ex)
        {
            RuleList.ToolTip = $"Rule parsing failed: {ex.Message}";
            OutputBox.Text =
                $"Goal {(_currentGoal?.Id.ToString() ?? "—")} — {_currentGoal?.Title ?? "(no Goal selected)"}" +
                Environment.NewLine + "Rule analysis failed:" + Environment.NewLine + ex.Message;
        }
    }

    private void RuleSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterRuleList();

    private void FilterRuleList()
    {
        string query = RuleSearchBox.Text.Trim();
        RuleList.Items.Clear();
        foreach (var entry in _allRuleEntries)
            if (query.Length == 0 || entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                RuleList.Items.Add(entry);
        RuleSearchBox.ToolTip = query.Length == 0
            ? $"Showing all {_allRuleEntries.Count:N0} rules in this Goal"
            : $"Showing {RuleList.Items.Count:N0} of {_allRuleEntries.Count:N0} rules";
    }

    private void BuildCrossReferenceIndex()
    {
        _crossReferenceEntries.Clear();
        if (_catalog is null || _source is null)
            return;

        foreach (var goal in _catalog.Goals)
            AddCrossReferencesForGoal(goal, null);

        FilterCrossReferenceList();
    }

    private void UpdateCrossReferencesForCurrentGoal()
    {
        if (_currentGoal is null)
            return;

        _crossReferenceEntries.RemoveAll(entry => entry.Goal.Id == _currentGoal.Id);
        AddCrossReferencesForGoal(_currentGoal, KbEditor.Text);
        FilterCrossReferenceList();
    }

    private void AddCrossReferencesForGoal(GoalSymbol goal, string? displayedKb)
    {
        if (_source is null)
            return;

        var kb = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.KnowledgeBaseSection);
        if (kb is null)
            return;

        string kbText = displayedKb ??
            (_edits.TryGetValue(kb.Start, out var edit)
                ? edit.Text : _source.Substring(kb.Start, kb.Length));

        var tokens = Lexer.Tokenize(kbText);
        var structure = new StructureParser().Parse(kbText, tokens);
        var parsed = new RuleParser().Parse(kbText, tokens, structure.Blocks);

        int number = 0;
        foreach (var rule in parsed.Rules)
        {
            number++;
            string raw = kbText.Substring(rule.Start, rule.Length);
            string callNames = string.Join(", ", rule.Calls
                .Select(call => call.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5));
            string kind = rule.Kind == RuleKind.If ? "IF" : "PROC";
            string detail = rule.Kind == RuleKind.If
                ? rule.Conditions.FirstOrDefault()?.Text ?? "(no condition)"
                : rule.Actions.FirstOrDefault()?.Text ?? "(no action)";
            string title = goal.Title ?? "(untitled)";
            string label = $"Goal {goal.Id} — {title}  |  {kind} #{number:D3}  |  " +
                (callNames.Length > 0 ? callNames + "  |  " : "") +
                CompactRuleText(detail, 72);
            var firstCondition = rule.Conditions.FirstOrDefault();
            var procedureHead = rule.Kind == RuleKind.Proc && firstCondition is not null
                ? rule.Calls
                    .Where(call => call.Start >= firstCondition.Start &&
                        call.Start < firstCondition.Start + firstCondition.Length &&
                        call.Name.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(call => call.Start)
                    .FirstOrDefault()
                : null;
            var procedureCalls = rule.Calls
                .Where(call => call.Name.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase) &&
                    (procedureHead is null || call.Start != procedureHead.Start))
                .Select(call => call.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string searchable = raw + " " + callNames + " " + goal.Id + " " + title;
            _crossReferenceEntries.Add(new CrossReferenceEntry(
                label, rule.Start, rule.Length, goal, searchable,
                rule.Calls.Select(call => call.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                procedureCalls, procedureHead?.Name));
        }
    }

    private void BuildGoalDependencyExplorer()
    {
        GoalLinkList.ItemsSource = null;
        GoalLinkList.Items.Clear();
        GoalLinkDetailsBox.Clear();
        GoalLinkEvidenceList.ItemsSource = null;
        GoalLinkEvidenceList.Items.Clear();
        _goalLinkEntries.Clear();
        _goalDependencyEvidenceEntries.Clear();

        if (_catalog is null || _source is null)
        {
            _goalDependencyAnalysis = null;
            GoalLinkDetailsBox.Text = "Open a Story.div source to inspect Goal dependencies.";
            RenderGoalLinkGraph();
            return;
        }

        var analyzer = new GoalDependencyAnalyzer();
        if (_goalDependencyAnalysis is null ||
            !string.Equals(_goalDependencyAnalysisSource, _source, StringComparison.Ordinal))
        {
            var editedKnowledgeBases = new Dictionary<int, string>();
            foreach (var goal in _catalog.Goals)
            {
                var kb = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.KnowledgeBaseSection);
                if (kb is not null && _edits.TryGetValue(kb.Start, out var edit))
                    editedKnowledgeBases[goal.Id] = edit.Text;
            }

            _goalDependencyAnalysis = analyzer.Analyze(_source, _catalog, editedKnowledgeBases);
            _goalDependencyKbSnapshots.Clear();
            foreach (var goal in _catalog.Goals)
            {
                var kb = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.KnowledgeBaseSection);
                if (kb is null) continue;
                _goalDependencyKbSnapshots[goal.Id] = editedKnowledgeBases.TryGetValue(goal.Id, out var edited)
                    ? edited : _source.Substring(kb.Start, kb.Length);
            }
            _goalDependencyAnalysisSource = _source;
        }

        if (_currentGoal is not null)
        {
            string currentKbText = KbEditor.Text;
            if (!_goalDependencyKbSnapshots.TryGetValue(_currentGoal.Id, out var previousText) ||
                !string.Equals(previousText, currentKbText, StringComparison.Ordinal))
            {
                bool procedureIndexMayHaveChanged =
                    previousText?.Contains("PROC_CompleteGoal", StringComparison.OrdinalIgnoreCase) == true ||
                    currentKbText.Contains("PROC_CompleteGoal", StringComparison.OrdinalIgnoreCase);

                if (procedureIndexMayHaveChanged)
                {
                    var editedKnowledgeBases = new Dictionary<int, string>();
                    foreach (var goal in _catalog.Goals)
                    {
                        var kb = goal.Block.Children.FirstOrDefault(
                            b => b.Kind == SyntaxKind.KnowledgeBaseSection);
                        if (kb is not null && _edits.TryGetValue(kb.Start, out var edit))
                            editedKnowledgeBases[goal.Id] = edit.Text;
                    }
                    editedKnowledgeBases[_currentGoal.Id] = currentKbText;

                    _goalDependencyAnalysis = analyzer.Analyze(
                        _source, _catalog, editedKnowledgeBases);
                    _goalDependencyKbSnapshots.Clear();
                    foreach (var goal in _catalog.Goals)
                    {
                        var kb = goal.Block.Children.FirstOrDefault(
                            b => b.Kind == SyntaxKind.KnowledgeBaseSection);
                        if (kb is null) continue;
                        _goalDependencyKbSnapshots[goal.Id] =
                            editedKnowledgeBases.TryGetValue(goal.Id, out var edited)
                                ? edited : _source.Substring(kb.Start, kb.Length);
                    }
                    _goalDependencyAnalysisSource = _source;
                }
                else
                {
                    var updatedGoal = analyzer.AnalyzeGoal(
                        _source, _catalog, _currentGoal, currentKbText,
                        _goalDependencyAnalysis.ProcedureHandlers);
                    var updatedReferences = _goalDependencyAnalysis.References
                        .Where(reference => reference.SourceGoal.Id != _currentGoal.Id)
                        .Concat(updatedGoal.References);
                    var updatedDiagnostics = _goalDependencyAnalysis.Diagnostics
                        .Where(diagnostic => !diagnostic.Message.StartsWith(
                            $"Goal {_currentGoal.Id}:", StringComparison.Ordinal))
                        .Concat(updatedGoal.Diagnostics);

                    _goalDependencyAnalysis = new GoalDependencyAnalysisResult(
                        updatedReferences.OrderBy(reference => reference.SourceGoal.Id)
                            .ThenBy(reference => reference.RuleStart)
                            .ThenBy(reference => reference.CallStart)
                            .ToArray(),
                        updatedDiagnostics.ToArray(),
                        _goalDependencyAnalysis.ProcedureHandlers);
                }
                _goalDependencyKbSnapshots[_currentGoal.Id] = currentKbText;
            }
        }
        if (_currentGoal is null)
        {
            GoalLinkDetailsBox.Text = "Select a Goal in the hierarchy to inspect explicit Goal-state references.";
            RenderGoalLinkGraph();
            return;
        }

        var currentCalls = _crossReferenceEntries
            .Where(entry => entry.Goal.Id == _currentGoal.Id)
            .SelectMany(entry => entry.Calls)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allResolved = _goalDependencyAnalysis.References
            .Where(reference => reference.IsResolved &&
                reference.SourceGoal.Id != reference.TargetGoal!.Id &&
                (reference.SourceGoal.Id == _currentGoal.Id ||
                 reference.TargetGoal.Id == _currentGoal.Id))
            .ToArray();

        var neighborIds = allResolved
            .Select(reference => reference.SourceGoal.Id == _currentGoal.Id
                ? reference.TargetGoal!.Id : reference.SourceGoal.Id)
            .ToHashSet();

        var sharedByGoal = _crossReferenceEntries
            .Where(entry => entry.Goal.Id != _currentGoal.Id)
            .GroupBy(entry => entry.Goal.Id)
            .Select(group => new
            {
                Goal = group.First().Goal,
                Shared = group.SelectMany(entry => entry.Calls)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(currentCalls.Contains)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(item => item.Shared.Length > 0)
            .ToArray();

        foreach (var item in sharedByGoal)
            neighborIds.Add(item.Goal.Id);

        foreach (int neighborId in neighborIds)
        {
            var goal = _catalog.Goals.FirstOrDefault(candidate => candidate.Id == neighborId);
            if (goal is null) continue;

            var dependencies = allResolved.Where(reference =>
                reference.SourceGoal.Id == neighborId ||
                reference.TargetGoal?.Id == neighborId).ToArray();

            var shared = sharedByGoal.FirstOrDefault(item => item.Goal.Id == neighborId)?.Shared
                ?? Array.Empty<string>();

            string label =
                $"Goal {goal.Id} - {goal.Title ?? "(untitled)"}   " +
                $"[API {dependencies.Count(d => d.Confidence == GoalDependencyConfidence.ApiDeclared)}; " +
                $"PROC {dependencies.Count(d => d.Confidence == GoalDependencyConfidence.StoryProcedureHandler)}; " +
                $"candidate {dependencies.Count(d => d.Confidence == GoalDependencyConfidence.NameBasedCandidate)}; " +
                $"shared {shared.Length}]";

            _goalLinkEntries.Add(new GoalLinkEntry(goal, _currentGoal.Id, dependencies, shared, label));
        }

        _goalLinkEntries.Sort((left, right) =>
        {
            int compare = right.ConfirmedCount.CompareTo(left.ConfirmedCount);
            if (compare != 0) return compare;
            compare = right.ProcedureCount.CompareTo(left.ProcedureCount);
            if (compare != 0) return compare;
            compare = right.CandidateCount.CompareTo(left.CandidateCount);
            if (compare != 0) return compare;
            compare = right.SharedCalls.Count.CompareTo(left.SharedCalls.Count);
            return compare != 0 ? compare : left.Goal.Id.CompareTo(right.Goal.Id);
        });

        GoalLinkList.ItemsSource = _goalLinkEntries;

        var outgoingConfirmed = _goalDependencyAnalysis.References.Count(reference =>
            reference.SourceGoal.Id == _currentGoal.Id && reference.IsResolved &&
            reference.TargetGoal!.Id != _currentGoal.Id &&
            reference.Confidence == GoalDependencyConfidence.ApiDeclared);
        var incomingConfirmed = _goalDependencyAnalysis.References.Count(reference =>
            reference.TargetGoal?.Id == _currentGoal.Id && reference.IsResolved &&
            reference.SourceGoal.Id != _currentGoal.Id &&
            reference.Confidence == GoalDependencyConfidence.ApiDeclared);
        var currentCandidates = _goalDependencyAnalysis.References.Count(reference =>
            reference.SourceGoal.Id == _currentGoal.Id && reference.IsResolved &&
            reference.TargetGoal!.Id != _currentGoal.Id &&
            reference.Confidence == GoalDependencyConfidence.NameBasedCandidate);
        var incomingCandidates = _goalDependencyAnalysis.References.Count(reference =>
            reference.TargetGoal?.Id == _currentGoal.Id && reference.IsResolved &&
            reference.SourceGoal.Id != _currentGoal.Id &&
            reference.Confidence == GoalDependencyConfidence.NameBasedCandidate);
        var currentProcedures = _goalDependencyAnalysis.References.Count(reference =>
            reference.SourceGoal.Id == _currentGoal.Id && reference.IsResolved &&
            reference.TargetGoal!.Id != _currentGoal.Id &&
            reference.Confidence == GoalDependencyConfidence.StoryProcedureHandler);
        var incomingProcedures = _goalDependencyAnalysis.References.Count(reference =>
            reference.TargetGoal?.Id == _currentGoal.Id && reference.IsResolved &&
            reference.SourceGoal.Id != _currentGoal.Id &&
            reference.Confidence == GoalDependencyConfidence.StoryProcedureHandler);
        var selfReferences = _goalDependencyAnalysis.References
            .Where(reference => reference.SourceGoal.Id == _currentGoal.Id &&
                reference.TargetGoal?.Id == _currentGoal.Id)
            .ToArray();
        var unresolved = _goalDependencyAnalysis.References
            .Where(reference => reference.SourceGoal.Id == _currentGoal.Id && !reference.IsResolved)
            .ToArray();

        GoalLinkDetailsBox.Text =
            $"Goal {_currentGoal.Id} - {_currentGoal.Title ?? "(untitled)"}" + Environment.NewLine +
            $"API-confirmed references: {outgoingConfirmed} outgoing, {incomingConfirmed} incoming" + Environment.NewLine +
            $"Story PROC_CompleteGoal handlers: {currentProcedures} outgoing, {incomingProcedures} incoming" + Environment.NewLine +
            $"Title-only candidates: {currentCandidates} outgoing, {incomingCandidates} incoming" + Environment.NewLine +
            $"Self-target references: {selfReferences.Length}" + Environment.NewLine +
            $"Unresolved Goal-name references: {unresolved.Length}" + Environment.NewLine +
            $"Shared-call heuristic neighbors: {sharedByGoal.Length}" + Environment.NewLine +
            $"Analyzer diagnostics: {_goalDependencyAnalysis.Diagnostics.Count}" + Environment.NewLine +
            Environment.NewLine +
            "Solid links = API-declared Goal operations; teal dashed = calls resolved to actual PROC_CompleteGoal handlers; orange dashed = title-only candidates; dotted = shared-call overlap." + Environment.NewLine +
            "PROC_CompleteGoal is a Story.div Osiris procedure. Its handler is resolved from a PROC rule head, not from a title guess. GoalCompleted completes the handler's owning Goal." + Environment.NewLine +
            "Select a neighboring Goal to inspect evidence. Double-click an evidence row to open its exact source location.";

        foreach (var reference in unresolved)
        {
            string confidence = reference.Confidence switch
            {
                GoalDependencyConfidence.ApiDeclared => "API reference",
                GoalDependencyConfidence.StoryProcedureHandler => "Story procedure",
                _ => "title-only candidate"
            };
            _goalDependencyEvidenceEntries.Add(new GoalDependencyEvidenceEntry(null,
                $"UNRESOLVED [{confidence}] {reference.FunctionName}({reference.TargetText}) - {reference.HandlerSummary ?? "no matching Goal handler"}"));
        }

        foreach (var reference in selfReferences)
        {
            string confidence = reference.Confidence switch
            {
                GoalDependencyConfidence.ApiDeclared => "API",
                GoalDependencyConfidence.StoryProcedureHandler => "STORY PROC",
                _ => "CANDIDATE"
            };
            _goalDependencyEvidenceEntries.Add(new GoalDependencyEvidenceEntry(reference,
                $"[SELF][{confidence}] {reference.FunctionName}({reference.TargetText}) - this Goal targets itself"));
        }

        GoalLinkEvidenceList.ItemsSource = _goalDependencyEvidenceEntries;

        if (_goalLinkEntries.Count == 0)
        {
            GoalLinkDetailsBox.Text += Environment.NewLine + Environment.NewLine +
                "No resolved Goal dependencies or shared-call overlaps were found.";
        }

        RenderGoalLinkGraph();
    }

    private void GoalLinkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GoalLinkDetailsBox is null || GoalLinkList.SelectedItem is not GoalLinkEntry selected)
            return;

        GoalLinkDetailsBox.Text =
            $"Current Goal: {_currentGoal?.Id} - {_currentGoal?.Title}" + Environment.NewLine +
            $"Related Goal: {selected.Goal.Id} - {selected.Goal.Title}" + Environment.NewLine +
            $"API-declared references: {selected.ConfirmedCount} " +
            $"(current -> related: {selected.OutgoingConfirmedCount}; related -> current: {selected.IncomingConfirmedCount})" + Environment.NewLine +
            $"Story PROC_CompleteGoal handlers: {selected.ProcedureCount} " +
            $"(current -> related: {selected.OutgoingProcedureCount}; related -> current: {selected.IncomingProcedureCount})" + Environment.NewLine +
            $"Title-only candidates: {selected.CandidateCount}" + Environment.NewLine +
            $"Shared call names (heuristic only): {selected.SharedCalls.Count}" + Environment.NewLine +
            Environment.NewLine +
            "Evidence is listed below. Double-click a row to open its exact KB, INIT or EXIT source location.";

        _goalDependencyEvidenceEntries.Clear();

        foreach (var dependency in selected.Dependencies
            .OrderBy(d => d.Confidence)
            .ThenBy(d => d.SourceGoal.Id)
            .ThenBy(d => d.RuleStart))
        {
            string direction = dependency.SourceGoal.Id == _currentGoal?.Id
                ? "OUTGOING" : "INCOMING";
            string confidence = dependency.Confidence switch
            {
                GoalDependencyConfidence.ApiDeclared => "API",
                GoalDependencyConfidence.StoryProcedureHandler => "STORY PROC",
                _ => "CANDIDATE"
            };
            string operation = dependency.Kind switch
            {
                GoalDependencyKind.Activates => "activates",
                GoalDependencyKind.Completes => "completes",
                GoalDependencyKind.SetsSleeping => "sets sleeping",
                GoalDependencyKind.ChecksActive => "checks active state",
                GoalDependencyKind.ChecksCompleted => "checks completed state",
                GoalDependencyKind.ChecksSleeping => "checks sleeping state",
                GoalDependencyKind.ReadsStatus => "reads status",
                GoalDependencyKind.ChecksWasActive => "checks previous active state",
                GoalDependencyKind.ChecksWasCompleted => "checks previous completed state",
                GoalDependencyKind.ChecksWasDefined => "checks whether defined",
                GoalDependencyKind.ChecksWasSleeping => "checks previous sleeping state",
                GoalDependencyKind.InvokesProcedure =>
                    $"dispatches to handler: {dependency.HandlerSummary ?? "handler found"}",
                _ => "references"
            };
            string location = dependency.SectionName == "KB"
                ? (dependency.RuleKind == RuleKind.If ? "IF" : "PROC")
                : dependency.SectionName;
            string area = dependency.SectionName == "KB"
                ? (dependency.IsCondition ? "condition" : "action/body")
                : "section body";
            string locationType = dependency.SectionName == "KB" ? "rule" : "section";
            string label =
                $"[{confidence}] {direction} {dependency.FunctionName}({dependency.TargetText}) - {operation} | " +
                $"Goal {dependency.SourceGoal.Id}, {location} {locationType}, {area}";

            _goalDependencyEvidenceEntries.Add(new GoalDependencyEvidenceEntry(dependency, label));
        }

        foreach (string sharedCall in selected.SharedCalls)
            _goalDependencyEvidenceEntries.Add(new GoalDependencyEvidenceEntry(null,
                $"[HEURISTIC] Shared call symbol: {sharedCall} - overlap only, not a proven Goal dependency"));

        GoalLinkEvidenceList.ItemsSource = null;
        GoalLinkEvidenceList.ItemsSource = _goalDependencyEvidenceEntries;
        RenderGoalLinkGraph();
    }

    private void GoalLinkEvidenceList_MouseDoubleClick(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GoalLinkEvidenceList.SelectedItem is GoalDependencyEvidenceEntry
            { Dependency: { } dependency })
            NavigateToGoalDependency(dependency);
    }

    private void NavigateToGoalDependency(GoalDependencyReference dependency)
    {
        var treeItem = FindGoalTreeItem(dependency.SourceGoal.Id);
        if (treeItem is not null)
            treeItem.IsSelected = true;

        ShowGoal(dependency.SourceGoal);

        if (dependency.SectionName == "KB")
        {
            SectionTabs.SelectedIndex = 0;
            var ruleEntry = _allRuleEntries.FirstOrDefault(entry =>
                entry.Start == dependency.RuleStart && entry.Length == dependency.RuleLength);
            if (ruleEntry is not null)
            {
                RuleList.SelectedItem = ruleEntry;
                RuleList.ScrollIntoView(ruleEntry);
                StatusText.Text =
                    $"Opened {dependency.FunctionName} reference in Goal {dependency.SourceGoal.Id}";
            }
            else
            {
                StatusText.Text =
                    $"Opened Goal {dependency.SourceGoal.Id}; matching KB rule was not found";
            }
            return;
        }

        ICSharpCode.AvalonEdit.TextEditor? editor = dependency.SectionName switch
        {
            "INIT" => InitEditor,
            "EXIT" => ExitEditor,
            _ => null
        };
        if (editor is null)
        {
            StatusText.Text = $"Opened Goal {dependency.SourceGoal.Id}; unsupported section {dependency.SectionName}";
            return;
        }

        SectionTabs.SelectedIndex = dependency.SectionName == "INIT" ? 1 : 2;
        int start = Math.Clamp(dependency.CallStart, 0, editor.Document.TextLength);
        int length = Math.Clamp(dependency.CallLength, 0, editor.Document.TextLength - start);
        editor.Select(start, length);
        editor.ScrollToLine(editor.Document.GetLineByOffset(start).LineNumber);
        editor.Focus();
        StatusText.Text =
            $"Opened {dependency.FunctionName} reference in Goal {dependency.SourceGoal.Id} {dependency.SectionName}";
    }
    private void GoalLinkGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderGoalLinkGraph();

    private void RenderGoalLinkGraph()
    {
        if (GoalLinkGraphCanvas is null) return;
        var canvas = GoalLinkGraphCanvas;
        canvas.Children.Clear();
        double width = canvas.ActualWidth > 100 ? canvas.ActualWidth : 700;
        double height = canvas.ActualHeight > 100 ? canvas.ActualHeight : 340;
        double cx = width / 2, cy = height / 2;
        const double centerW = 154, centerH = 54;

        var center = new Button
        {
            Width = centerW, Height = centerH, IsEnabled = false,
            Content = new TextBlock
            {
                Text = _currentGoal is null ? "Select a Goal" :
                    $"Goal {_currentGoal.Id}\n{CompactRuleText(_currentGoal.Title ?? "(untitled)", 22)}",
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White, FontSize = 11
            },
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 79, 120)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(86, 156, 214))
        };
        Canvas.SetLeft(center, cx - centerW / 2);
        Canvas.SetTop(center, cy - centerH / 2);
        canvas.Children.Add(center);

        if (_currentGoal is null || _goalLinkEntries.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = _currentGoal is null ? "Choose a Goal to build its relationship map" :
                    "No Goal dependency or shared-call relationships found",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)),
                FontSize = 12
            };
            Canvas.SetLeft(empty, 12); Canvas.SetTop(empty, 12); canvas.Children.Add(empty);
            return;
        }

        var visible = _goalLinkEntries.Take(12).ToArray();
        double rx = Math.Max(90, Math.Min(width * 0.39, width / 2 - 95));
        double ry = Math.Max(70, Math.Min(height * 0.37, height / 2 - 45));
        int? selectedId = (GoalLinkList.SelectedItem as GoalLinkEntry)?.Goal.Id;

        for (int i = 0; i < visible.Length; i++)
        {
            var entry = visible[i];
            double angle = -Math.PI / 2 + 2 * Math.PI * i / visible.Length;
            double x = cx + Math.Cos(angle) * rx, y = cy + Math.Sin(angle) * ry;
            var edgeBrush = entry.ConfirmedCount > 0
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(86, 156, 214))
                : entry.ProcedureCount > 0
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 201, 176))
                    : entry.CandidateCount > 0
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(215, 150, 90))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(110, 110, 110));
            var edge = new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = x, Y2 = y,
                Stroke = edgeBrush,
                StrokeThickness = Math.Min(5, 1 + (entry.ConfirmedCount + entry.ProcedureCount +
                    entry.CandidateCount + entry.SharedCalls.Count) * 0.25),
                Opacity = 0.9
            };
            if (entry.ConfirmedCount == 0 && (entry.ProcedureCount > 0 || entry.CandidateCount > 0))
                edge.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 2 };
            else if (entry.ConfirmedCount == 0 && entry.ProcedureCount == 0 && entry.CandidateCount == 0)
                edge.StrokeDashArray = new System.Windows.Media.DoubleCollection { 1, 3 };
            canvas.Children.Add(edge);
            var count = new TextBlock
            {
                Text = entry.Dependencies.Count > 0
                    ? $"->{entry.OutgoingCount} / <-{entry.IncomingCount}"
                    : "~" + entry.SharedCalls.Count,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(215, 186, 125)),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32)),
                FontSize = 10, Padding = new Thickness(3, 1, 3, 1)
            };
            Canvas.SetLeft(count, (cx + x) / 2 - 8); Canvas.SetTop(count, (cy + y) / 2 - 8);
            canvas.Children.Add(count);
        }

        for (int i = 0; i < visible.Length; i++)
        {
            var entry = visible[i];
            double angle = -Math.PI / 2 + 2 * Math.PI * i / visible.Length;
            double x = cx + Math.Cos(angle) * rx, y = cy + Math.Sin(angle) * ry;
            bool selected = selectedId == entry.Goal.Id;
            var node = new Button
            {
                Width = 132, Height = 48, Tag = entry.Goal,
                ToolTip = $"{entry.Goal.Title}\nAPI references: {entry.ConfirmedCount}\nStory PROC handlers: {entry.ProcedureCount}\nTitle-only candidates: {entry.CandidateCount}\nShared-call overlap: {entry.SharedCalls.Count}\nClick to open this Goal",
                Content = new TextBlock
                {
                    Text = $"Goal {entry.Goal.Id}\n{CompactRuleText(entry.Goal.Title ?? "(untitled)", 18)}",
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.White, FontSize = 10
                },
                Background = new System.Windows.Media.SolidColorBrush(selected
                    ? System.Windows.Media.Color.FromRgb(55, 86, 112) : System.Windows.Media.Color.FromRgb(45, 45, 48)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(selected
                    ? System.Windows.Media.Color.FromRgb(86, 156, 214) : System.Windows.Media.Color.FromRgb(78, 201, 176)),
                BorderThickness = new Thickness(selected ? 2 : 1), Padding = new Thickness(3)
            };
            node.Click += GoalLinkGraphNode_Click;
            Canvas.SetLeft(node, x - node.Width / 2); Canvas.SetTop(node, y - node.Height / 2);
            canvas.Children.Add(node);
        }

        var footer = new TextBlock
        {
            Text = $"Showing {visible.Length} of {_goalLinkEntries.Count} related Goals | solid = API, teal dashed = Story PROC, orange dashed = title-only, dotted = overlap",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
            FontSize = 10
        };
        Canvas.SetLeft(footer, 10); Canvas.SetTop(footer, Math.Max(8, height - 24)); canvas.Children.Add(footer);
    }

    private void GoalLinkGraphNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GoalSymbol goal }) return;
        var treeItem = FindGoalTreeItem(goal.Id);
        if (treeItem is not null) treeItem.IsSelected = true;
        ShowGoal(goal);
        StatusText.Text = $"Opened related Goal {goal.Id} from relationship map";
    }
    private void GoalLinkList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GoalLinkList.SelectedItem is not GoalLinkEntry selected)
            return;

        var treeItem = FindGoalTreeItem(selected.Goal.Id);
        if (treeItem is not null)
            treeItem.IsSelected = true;
        ShowGoal(selected.Goal);
        StatusText.Text = $"Navigated to related Goal {selected.Goal.Id}";
    }
    private void BuildSymbolExplorer()
    {
        string? selectedName = (SymbolList.SelectedItem as SymbolExplorerEntry)?.Name;
        _symbolEntries.Clear();
        if (_source is null || _api is null) return;

        var signaturePattern = new Regex(
            @"^\s*(event|query|call|sysquery|syscall)\s+([A-Za-z_]\w*)\s*(\([^)]*\))",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var signatures = signaturePattern.Matches(_source).Cast<Match>()
            .GroupBy(m => m.Groups[2].Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Groups[3].Value, StringComparer.Ordinal);

        foreach (var pair in _api.Functions.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string kind = pair.Value switch
            {
                ApiSymbolKind.Event => "Event",
                ApiSymbolKind.Query => "Query",
                ApiSymbolKind.Call => "Call",
                ApiSymbolKind.SystemQuery => "System query",
                ApiSymbolKind.SystemCall => "System call",
                _ => "API"
            };
            AddSymbol(pair.Key, kind, signatures.TryGetValue(pair.Key, out var sig) ? sig : "(signature unavailable)");
        }
        foreach (string name in _api.Objects.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            AddSymbol(name, "Object", "");
        foreach (string name in _api.Types.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            AddSymbol(name, "Type", "");

        // Classify names missing from the API text using the installed game's DBase name table.
        // A DBase match confirms a game database symbol name, not an Osiris API signature.
        var declaredNames = new HashSet<string>(_api.Functions.Keys, StringComparer.OrdinalIgnoreCase);
        declaredNames.UnionWith(_api.Objects);
        declaredNames.UnionWith(_api.Types);
        var allCallNames = _crossReferenceEntries.Concat(_procedureLifecycleCallEntries)
            .SelectMany(entry => entry.Calls)
            .Where(name => !declaredNames.Contains(name) &&
                !name.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string name in GameDatabaseSymbolNames.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            string[] records = GameDatabaseSymbolRecords.Entries.TryGetValue(name, out var found)
                ? found
                : Array.Empty<string>();
            string apiSignature = signatures.TryGetValue(name, out var declaredSignature)
                ? declaredSignature
                : "";
            var display = BuildGameDatabaseSymbolDisplay(records, apiSignature);
            AddSymbol(name, "Game database symbol", display.Signature, display.Summary);
        }

        foreach (string name in allCallNames.Where(name => !GameDatabaseSymbolNames.Names.Contains(name)))
        {
            int occurrences = _crossReferenceEntries.Concat(_procedureLifecycleCallEntries)
                .Sum(entry => entry.Calls.Count(call =>
                    string.Equals(call, name, StringComparison.OrdinalIgnoreCase)));
            AddSymbol(name, "Unresolved call", "(not declared in known catalogs)",
                $"{occurrences:N0} call occurrence(s) not found in the API or DBase name tables; review manually");
        }

        // Story-defined Osiris procedures are declarations in PROC rule heads,
        // not exported engine API symbols. Keep them in a separate category.
        if (_catalog is not null)
        {
            var storyProcedures = new Dictionary<string, List<(GoalSymbol Goal, int Start, int Length, string Signature)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var goal in _catalog.Goals)
            {
                var kb = goal.Block.Children.FirstOrDefault(b => b.Kind == SyntaxKind.KnowledgeBaseSection);
                if (kb is null) continue;
                string kbText = goal.Id == _currentGoal?.Id
                    ? KbEditor.Text
                    : _edits.TryGetValue(kb.Start, out var editedKb)
                        ? editedKb.Text
                        : _source.Substring(kb.Start, kb.Length);
                var procedureTokens = Lexer.Tokenize(kbText);
                var procedureStructure = new StructureParser().Parse(kbText, procedureTokens);
                var procedureParse = new RuleParser().Parse(kbText, procedureTokens, procedureStructure.Blocks);
                foreach (var rule in procedureParse.Rules)
                {
                    if (rule.Kind != RuleKind.Proc || rule.Conditions.Count == 0) continue;
                    var firstCondition = rule.Conditions[0];
                    var head = rule.Calls
                        .Where(call => call.Start >= firstCondition.Start &&
                            call.Start < firstCondition.Start + firstCondition.Length)
                        .Where(call => call.Name.StartsWith("PROC_", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(call => call.Start)
                        .FirstOrDefault();
                    if (head is null) continue;

                    if (!storyProcedures.TryGetValue(head.Name, out var definitions))
                        storyProcedures[head.Name] = definitions = new();
                    string signature = "(" + string.Join(", ", head.Arguments.Select(a => a.Text.Trim())) + ")";
                    definitions.Add((goal, rule.Start, rule.Length, signature));
                }
            }

            foreach (var pair in storyProcedures.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var definitions = pair.Value;
                string owners = string.Join(", ", definitions.Select(d => $"Goal {d.Goal.Id}").Distinct());
                var handlerLocations = definitions
                    .Select(d => (d.Goal.Id, d.Start, d.Length))
                    .ToHashSet();
                int otherReferenceRules = _crossReferenceEntries.Count(entry =>
                    entry.Calls.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) &&
                    !handlerLocations.Contains((entry.Goal.Id, entry.Start, entry.Length)));
                string literalArguments = string.Join(", ",
                    definitions.SelectMany(d => Regex.Matches(d.Signature, "\"(?:\\\\.|[^\"])*\"")
                        .Cast<Match>().Select(m => m.Value))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(12));
                string summary = $"{definitions.Count} handler rule(s); {otherReferenceRules} other KB rule(s) reference it; defined in {owners}" +
                    (literalArguments.Length == 0 ? "" : $"; literal arguments: {literalArguments}");
                var first = definitions.OrderBy(d => d.Goal.Id).ThenBy(d => d.Start).First();
                AddSymbol(pair.Key, "Story procedure", first.Signature, summary, first.Start, first.Length, first.Goal.Id);
            }
        }

        FilterSymbolList();
        if (selectedName is not null)
        {
            var selected = _symbolEntries.FirstOrDefault(entry =>
                string.Equals(entry.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                SymbolList.SelectedItem = selected;
                SymbolList.ScrollIntoView(selected);
            }
        }
        return;

        void AddSymbol(string name, string kind, string signature,
            string definitionSummary = "", int? definitionStart = null, int? definitionLength = null, int? definitionGoalId = null)
        {
            var pattern = new Regex(@"(?<![A-Za-z0-9_])" + Regex.Escape(name) +
                @"(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var indexedEntries = _crossReferenceEntries.Concat(_procedureLifecycleCallEntries);
            int usage = kind == "Story procedure"
                ? indexedEntries.Count(entry =>
                    entry.ProcedureCalls.Contains(name, StringComparer.OrdinalIgnoreCase))
                : indexedEntries.Count(entry => pattern.IsMatch(entry.SearchText));
            _symbolEntries.Add(new SymbolExplorerEntry(name, kind, signature, usage,
                $"{kind,-15} {name}   ({usage:N0} rule(s))",
                definitionSummary, definitionStart, definitionLength, definitionGoalId));
        }
    }

    private static (string Signature, string Summary) BuildGameDatabaseSymbolDisplay(
        IReadOnlyList<string> records, string apiSignature)
    {
        var typeNames = new Dictionary<int, string>
        {
            [1] = "INTEGER", [2] = "REAL", [3] = "STRING", [4] = "NPC",
            [5] = "OBJECT", [6] = "DIALOG", [7] = "REGION", [8] = "LOCATION",
            [9] = "NPC_CLASS", [10] = "OBJECT_CLASS", [11] = "DIALOG_EVENT",
            [12] = "ENGINE", [13] = "FUNCTION", [15] = "SREGION"
        };

        var lines = new List<string>();
        var signatures = new List<string>();
        foreach (string record in records)
        {
            var flagMatch = Regex.Match(record, @"\bflag=(\d+)");
            var valuesMatch = Regex.Match(record, @"\bvalueTypes=\[([^\]]*)\]");
            string flag = flagMatch.Success ? flagMatch.Groups[1].Value : "?";
            string rawCodes = valuesMatch.Success ? valuesMatch.Groups[1].Value : "";
            string[] codes = rawCodes.Length == 0 ? Array.Empty<string>() :
                rawCodes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string[] names = codes.Select(code =>
                int.TryParse(code, out int value) && typeNames.TryGetValue(value, out string? type)
                    ? type : $"UNKNOWN_TYPE_{code}").ToArray();
            string signature = "(" + string.Join(", ", names) + ")";
            string[] apiTypes = Regex.Matches(apiSignature, @"\[(?:in|out)\]\(([^)]+)\)", RegexOptions.IgnoreCase)
                .Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
            bool matchesApi = apiTypes.Length == names.Length &&
                apiTypes.SequenceEqual(names, StringComparer.OrdinalIgnoreCase);
            string status = apiSignature.Length == 0
                ? "no textual API declaration"
                : matchesApi ? "exact API type match" : "differs from API declaration";
            signatures.Add($"flag {flag}: {signature}");
            lines.Add($"  flag {flag}: {signature}  [{status}]  | raw valueTypes=[{rawCodes}]");
        }

        string signatureText = signatures.Count == 0
            ? "(DBase signature unavailable)"
            : string.Join("; ", signatures);
        string apiText = apiSignature.Length == 0 ? "(none found)" : apiSignature;
        string summary = "DBase descriptors:" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "Story.div API declaration: " + apiText + Environment.NewLine +
            "Raw records: " + string.Join(" | ", records);
        return (signatureText, summary);
    }
    private void SymbolFilter_Changed(object sender, RoutedEventArgs e) => FilterSymbolList();

    private void FilterSymbolList()
    {
        if (SymbolList is null || SymbolSearchBox is null || SymbolKindFilter is null) return;
        string query = SymbolSearchBox.Text.Trim();
        string kind = (SymbolKindFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All symbols";
        SymbolList.Items.Clear();
        foreach (var symbol in _symbolEntries)
        {
            bool kindMatch = kind == "All symbols" || kind switch
            {
                "Events" => symbol.Kind == "Event",
                "Queries" => symbol.Kind == "Query",
                "Calls" => symbol.Kind == "Call",
                "System queries" => symbol.Kind == "System query",
                "System calls" => symbol.Kind == "System call",
                "Objects" => symbol.Kind == "Object",
                "Types" => symbol.Kind == "Type",
                "Story procedures" => symbol.Kind == "Story procedure",
                "Game database symbols" => symbol.Kind == "Game database symbol",
                "Unresolved calls" => symbol.Kind == "Unresolved call",
                _ => true
            };
            if (kindMatch && (query.Length == 0 ||
                symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                symbol.Signature.Contains(query, StringComparison.OrdinalIgnoreCase)))
                SymbolList.Items.Add(symbol);
        }
        SymbolSearchBox.ToolTip = $"Showing {SymbolList.Items.Count:N0} of {_symbolEntries.Count:N0} symbols";
    }

    private void SymbolList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SymbolDetailsBox is null) return;
        if (SymbolList.SelectedItem is not SymbolExplorerEntry symbol)
        {
            SymbolDetailsBox.Clear();
            SymbolUsageHeader.Text = "All usages";
            SymbolUsageList.Items.Clear();
            return;
        }

        bool isStoryProcedure = symbol.Kind == "Story procedure";
        var callMatches = isStoryProcedure
            ? _crossReferenceEntries
                .Where(entry => entry.ProcedureCalls.Contains(symbol.Name, StringComparer.OrdinalIgnoreCase))
                .ToList()
            : FindReferencesForSymbol(symbol.Name);
        var handlerMatches = isStoryProcedure
            ? _crossReferenceEntries
                .Where(entry => string.Equals(entry.ProcedureHeadName, symbol.Name, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<CrossReferenceEntry>();
        SymbolDetailsBox.Text =
            $"Name: {symbol.Name}{Environment.NewLine}" +
            $"Kind: {symbol.Kind}{Environment.NewLine}" +
            $"Signature: {(symbol.Signature.Length == 0 ? "(not applicable)" : symbol.Signature)}{Environment.NewLine}" +
            (isStoryProcedure
                ? $"Handler rules: {handlerMatches.Count:N0}{Environment.NewLine}Call sites: {callMatches.Count:N0} (handler declarations excluded)"
                : $"Usage: {callMatches.Count:N0} rule(s) in the indexed source") +
            (symbol.DefinitionSummary.Length == 0 ? "" : Environment.NewLine + $"Definitions: {symbol.DefinitionSummary}");
        SymbolUsageHeader.Text = isStoryProcedure
            ? $"[HANDLER] {handlerMatches.Count:N0} definition(s) | [CALL] {callMatches.Count:N0} call-site rule(s) — double-click to navigate"
            : $"All usages | {callMatches.Count:N0} matching rule(s) (double-click a rule to navigate)";
        SymbolUsageList.Items.Clear();
        foreach (var match in handlerMatches)
            SymbolUsageList.Items.Add(match with { Label = "[HANDLER] " + match.Label });
        foreach (var match in callMatches)
            SymbolUsageList.Items.Add(match with { Label = "[CALL] " + match.Label });
    }

    private List<CrossReferenceEntry> FindReferencesForSymbol(string name)
    {
        var pattern = new Regex(@"(?<![A-Za-z0-9_])" + Regex.Escape(name) +
            @"(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return _crossReferenceEntries.Concat(_procedureLifecycleCallEntries)
            .Where(entry => pattern.IsMatch(entry.SearchText)).ToList();
    }

    private void SymbolUsageList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SymbolUsageList.SelectedItem is CrossReferenceEntry selected)
            NavigateToCrossReference(selected);
    }

    private void SymbolList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SymbolList.SelectedItem is not SymbolExplorerEntry symbol) return;
        if (symbol.DefinitionStart is int definitionStart &&
            symbol.DefinitionLength is int definitionLength &&
            symbol.DefinitionGoalId is int definitionGoalId)
        {
            var treeItem = FindGoalTreeItem(definitionGoalId);
            if (treeItem is not null) treeItem.IsSelected = true;
            var definitionGoal = _catalog?.Goals.FirstOrDefault(goal => goal.Id == definitionGoalId);
            if (definitionGoal is not null) ShowGoal(definitionGoal);
            var definition = _allRuleEntries.FirstOrDefault(entry =>
                entry.Start == definitionStart && entry.Length == definitionLength);
            if (definition is not null)
            {
                RuleList.SelectedItem = definition;
                RuleList.ScrollIntoView(definition);
                StatusText.Text = $"Opened Story procedure definition: {symbol.Name} in Goal {definitionGoalId}";
                return;
            }
        }

        var match = FindReferencesForSymbol(symbol.Name).FirstOrDefault();
        if (match is null)
        {
            StatusText.Text = $"No indexed rule usage for {symbol.Name}";
            return;
        }
        NavigateToCrossReference(match);
    }
    private void CrossReferenceSearch_TextChanged(object sender, TextChangedEventArgs e) =>
        FilterCrossReferenceList();

    private void FilterCrossReferenceList()
    {
        if (CrossReferenceSearchBox is null || CrossReferenceList is null)
            return;

        string query = CrossReferenceSearchBox.Text.Trim();
        CrossReferenceList.Items.Clear();
        if (query.Length == 0)
        {
            CrossReferenceSearchBox.ToolTip =
                $"Search all {_crossReferenceEntries.Count:N0} indexed rules by function, object or text";
            return;
        }

        bool isKnownSymbol = _api is not null &&
            (_api.Functions.ContainsKey(query) || _api.Objects.Contains(query) || _api.Types.Contains(query));
        Regex? exactSymbol = isKnownSymbol
            ? new Regex(@"(?<![A-Za-z0-9_])" + Regex.Escape(query) + @"(?![A-Za-z0-9_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : null;

        foreach (var entry in _crossReferenceEntries)
        {
            bool match = exactSymbol is null
                ? entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
                : exactSymbol.IsMatch(entry.SearchText);
            if (match)
                CrossReferenceList.Items.Add(entry);
        }

        CrossReferenceSearchBox.ToolTip =
            $"Found {CrossReferenceList.Items.Count:N0} matching rules across all Goals";
    }

    private void CrossReferenceList_MouseDoubleClick(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CrossReferenceList.SelectedItem is not CrossReferenceEntry selected)
            return;

        NavigateToCrossReference(selected);
    }

    private void NavigateToCrossReference(CrossReferenceEntry selected)
    {
        var treeItem = FindGoalTreeItem(selected.Goal.Id);
        if (treeItem is not null) treeItem.IsSelected = true;
        ShowGoal(selected.Goal);
        SectionTabs.SelectedIndex = 0;

        var ruleEntry = _allRuleEntries.FirstOrDefault(entry =>
            entry.Start == selected.Start && entry.Length == selected.Length);
        if (ruleEntry is not null)
        {
            RuleList.SelectedItem = ruleEntry;
            RuleList.ScrollIntoView(ruleEntry);
            StatusText.Text = $"Reference opened: Goal {selected.Goal.Id}, {ruleEntry.Label}";
        }
        else StatusText.Text = $"Opened Goal {selected.Goal.Id}; indexed rule could not be matched";
    }
    private TreeViewItem? FindGoalTreeItem(int goalId)
    {
        foreach (TreeViewItem item in GoalTree.Items)
        {
            var found = FindGoalTreeItem(item, goalId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static TreeViewItem? FindGoalTreeItem(TreeViewItem item, int goalId)
    {
        if (item.Tag is GoalSymbol goal && goal.Id == goalId)
            return item;

        foreach (TreeViewItem child in item.Items)
        {
            var found = FindGoalTreeItem(child, goalId);
            if (found is not null)
            {
                item.IsExpanded = true;
                return found;
            }
        }
        return null;
    }

    private static string CompactRuleText(string text, int maxLength)
    {
        string compact = string.Join(" ", text.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..(maxLength - 1)] + "…";
    }

    private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RuleList.SelectedItem is not RuleListEntry rule)
            return;

        int start = Math.Clamp(rule.Start, 0, KbEditor.Document.TextLength);
        int length = Math.Clamp(rule.Length, 0, KbEditor.Document.TextLength - start);
        KbEditor.Focus();
        KbEditor.Select(start, length);
        KbEditor.ScrollToLine(KbEditor.Document.GetLineByOffset(start).LineNumber);
        StatusText.Text = $"Selected {rule.Label}";
    }

    private void RuleList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RuleList.SelectedItem is RuleListEntry)
            EditRule_Click(sender, e);
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleList.SelectedItem is not RuleListEntry selected)
        {
            MessageBox.Show(this, "Select a rule first.", "Rule editor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string originalRuleText = KbEditor.Text.Substring(selected.Start, selected.Length);
        var dialog = new RuleEditWindow(originalRuleText, selected.Rule.Kind, selected.Rule.Conditions, selected.Rule.Actions, selected.Rule.ProcedureSignature, selected.Start) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            int start = Math.Clamp(selected.Start, 0, KbEditor.Document.TextLength);
            int length = Math.Clamp(selected.Length, 0, KbEditor.Document.TextLength - start);
            string candidate = KbEditor.Text.Remove(start, length)
                .Insert(start, dialog.ResultText);

            // Validate the candidate before touching the live editor buffer.
            var candidateTokens = Lexer.Tokenize(candidate);
            var candidateStructure = new StructureParser().Parse(candidate, candidateTokens);
            var candidateRules = new RuleParser().Parse(candidate, candidateTokens,
                candidateStructure.Blocks);

            int expectedRuleCount = _allRuleEntries.Count;
            int selectedIndex = _allRuleEntries.FindIndex(entry =>
                entry.Start == selected.Start && entry.Length == selected.Length);
            bool targetRuleValid = selectedIndex >= 0 &&
                selectedIndex < candidateRules.Rules.Count &&
                candidateRules.Rules[selectedIndex].Start == start &&
                candidateRules.Rules[selectedIndex].Length > 0 &&
                candidateRules.Rules[selectedIndex].Kind == selected.Rule.Kind;
            bool untouchedRulesPreserved = candidateRules.Rules.Count == expectedRuleCount &&
                selectedIndex >= 0 &&
                Enumerable.Range(0, expectedRuleCount).Where(i => i != selectedIndex).All(i =>
                {
                    var before = _allRuleEntries[i];
                    var after = candidateRules.Rules[i];
                    if (after.Start < 0 || after.Length < 0 || after.Start + after.Length > candidate.Length)
                        return false;
                    return before.Rule.Kind == after.Kind &&
                        string.Equals(
                            KbEditor.Text.Substring(before.Start, before.Length),
                            candidate.Substring(after.Start, after.Length),
                            StringComparison.Ordinal);
                });

            if (candidateStructure.Diagnostics.Count > 0 ||
                candidateRules.Diagnostics.Count > 0 ||
                candidateRules.Rules.Count != expectedRuleCount ||
                !targetRuleValid || !untouchedRulesPreserved)
            {
                string diagnostics = string.Join(Environment.NewLine,
                    candidateStructure.Diagnostics.Select(d => d.Message)
                    .Concat(candidateRules.Diagnostics.Select(d => d.Message))
                    .Take(12));
                if (string.IsNullOrWhiteSpace(diagnostics))
                    diagnostics = !targetRuleValid
                        ? "The selected rule changed its IF/PROC type or could not be identified after parsing."
                        : !untouchedRulesPreserved
                            ? "The edit unexpectedly changed one or more neighboring rules."
                            : $"Expected {expectedRuleCount} rules, parser found {candidateRules.Rules.Count}.";
                MessageBox.Show(this,
                    "The edited rule was not applied because validation failed:" +
                    Environment.NewLine + Environment.NewLine + diagnostics,
                    "Rule validation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            KbEditor.Document.Replace(start, length, dialog.ResultText);
            KbEditor.Select(start, dialog.ResultText.Length);
            KbEditor.ScrollToLine(KbEditor.Document.GetLineByOffset(start).LineNumber);
            KbEditor.Focus();
            StatusText.Text = "Rule updated; structural parser checks passed";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not apply rule",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string Slice(SyntaxBlock? block) =>
        block is null || _source is null ? "" : _source.Substring(block.Start, block.Length);

    private void UpdateColorizer(TextEditor editor, OsirisColorizer colorizer)
    {
        colorizer.Update(editor.Text, _api);
        editor.TextArea.TextView.Redraw();
    }

    private async void BuildStory_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _sourcePath is null) return;

        var installation = GameInstallationSettings.Load();
        if (installation is null || !installation.Validate(out _))
            installation = GameInstallationSettings.PromptForInstallation(this);
        if (installation is null) return;

        if (!installation.Validate(out string installationError))
        {
            MessageBox.Show(this, installationError, "Osiris build unavailable",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string gameRoot = installation.GameRoot;
        string osirisDllPath = installation.OsirisDllPath;
        string baseStory = installation.BaseStoryPath;
        string scriptPath;
        try
        {
            scriptPath = BuildScriptProvider.EnsureExtracted();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "The embedded Osiris build scripts could not be prepared.\n\n" + ex.Message +
                "\n\nNo game files were changed.",
                "Osiris build unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string updated;
        try
        {
            updated = CreateUpdatedSource();
            var tokens = Lexer.Tokenize(updated);
            var structure = new StructureParser().Parse(updated, tokens);
            var rules = new RuleParser().Parse(updated, tokens, structure.Blocks);
            if (structure.Diagnostics.Count > 0 || rules.Diagnostics.Count > 0)
            {
                OutputBox.Text = "Build refused: source parser diagnostics were reported.\r\n" +
                    string.Join("\r\n", structure.Diagnostics.Select(d => d.Message)
                        .Concat(rules.Diagnostics.Select(d => d.Message)).Take(40));
                StatusText.Text = "Build blocked by source diagnostics";
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Could not prepare source",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string documentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documentsRoot))
            documentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string buildsRoot = Path.Combine(documentsRoot, "StoryEditor", "Builds");
        Directory.CreateDirectory(buildsRoot);
        var dialog = new SaveFileDialog
        {
            Title = "Build a separate test story.000",
            Filter = "Osiris story binary (*.000)|*.000|All files (*.*)|*.*",
            DefaultExt = "000",
            AddExtension = false,
            OverwritePrompt = false,
            FileName = "story_test.000",
            InitialDirectory = buildsRoot
        };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName))
        {
            MessageBox.Show(this, "The build script never overwrites an existing output. Choose a new filename.",
                "Output already exists", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string buildRoot = Path.Combine(buildsRoot, stamp);
        string sourceDirectory = Path.Combine(buildRoot, "Source");
        string workDirectory = Path.Combine(buildRoot, "Work");
        string logDirectory = Path.Combine(buildRoot, "Logs");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(logDirectory);
        string buildSource = Path.Combine(sourceDirectory, "Story_build.div");
        string logPath = Path.Combine(logDirectory, "OsirisBuild.log");
        File.WriteAllText(buildSource, updated, new System.Text.UTF8Encoding(false));

        BuildButton.IsEnabled = false;
        StatusText.Text = "Compiling with original OsirisDLL...";
        OutputBox.Text = "Starting original OsirisDLL compiler...\r\n" +
            "Base story: " + baseStory + "\r\n" +
            "Source snapshot: " + buildSource + "\r\n" +
            "Output: " + dialog.FileName + "\r\n";

        try
        {
            var result = await OsirisDllBuildService.BuildAsync(
                gameRoot, osirisDllPath, baseStory, buildSource, dialog.FileName, logPath, workDirectory, scriptPath);
            OutputBox.Text = result.StandardOutput +
                (string.IsNullOrWhiteSpace(result.StandardError) ? "" : "\r\nSTDERR:\r\n" + result.StandardError) +
                "\r\nBuild log: " + result.BuildLogPath +
                "\r\nValidation log: " + result.ValidationLogPath;

            if (result.Success)
            {
                StatusText.Text = "Osiris build succeeded — separate story file created";
                MessageBox.Show(this,
                    "OsirisDLL successfully compiled and saved the test story.\n\n" + result.OutputPath +
                    "\n\nThe output was structurally validated and was NOT installed into the game.",
                    "Build succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = "Osiris build failed — see build log";
                MessageBox.Show(this,
                    "OsirisDLL build failed (exit code " + result.ExitCode + ").\n\nSee:\n" +
                    result.BuildLogPath + "\n" + result.ValidationLogPath,
                    "Build failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.ToString();
            StatusText.Text = "Osiris build failed";
            MessageBox.Show(this, ex.ToString(), "Osiris build failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BuildButton.IsEnabled = _source is not null;
        }
    }

    private string CreateUpdatedSource()
    {
        if (_source is null) throw new InvalidOperationException("No source is loaded.");
        string updated = _source;
        foreach (var pair in _edits.OrderByDescending(p => p.Key))
        {
            string originalSection = _source.Substring(pair.Key, pair.Value.OriginalLength);
            string replacement = NormalizeLineEndings(pair.Value.Text, originalSection);
            updated = updated.Remove(pair.Key, pair.Value.OriginalLength)
                .Insert(pair.Key, replacement);
        }
        return updated;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || _sourcePath is null || (_edits.Count == 0 && !_hasStructuralEdits)) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save edited Osiris source as a separate copy",
            Filter = "Osiris source (*.div)|*.div|All files (*.*)|*.*",
            DefaultExt = "div",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_sourcePath) + "_edited.div",
            InitialDirectory = Path.GetDirectoryName(_sourcePath)
        };
        if (dialog.ShowDialog(this) != true) return;

        // Capture all UI-owned state before leaving the dispatcher thread.
        string sourceSnapshot = _source;
        var editSnapshot = _edits
            .Select(pair => (Start: pair.Key, pair.Value.OriginalLength, pair.Value.Text))
            .OrderByDescending(edit => edit.Start)
            .ToArray();
        int changedSectionCount = editSnapshot.Length;

        SaveButton.IsEnabled = false;
        StatusText.Text = "Preparing and validating Story.div in background...";
        OutputBox.Text = "Building the saved source snapshot and validating all rules. The editor remains available.";

        try
        {
            var validation = await Task.Run(() =>
            {
                string updated = sourceSnapshot;
                foreach (var edit in editSnapshot)
                {
                    string originalSection = sourceSnapshot.Substring(edit.Start, edit.OriginalLength);
                    string replacement = NormalizeLineEndings(edit.Text, originalSection);
                    updated = updated.Remove(edit.Start, edit.OriginalLength)
                        .Insert(edit.Start, replacement);
                }

                var tokens = Lexer.Tokenize(updated);
                var structure = new StructureParser().Parse(updated, tokens);
                var rules = new RuleParser().Parse(updated, tokens, structure.Blocks);
                string diagnostics = string.Join("\r\n",
                    structure.Diagnostics.Select(d => d.Message)
                        .Concat(rules.Diagnostics.Select(d => d.Message)).Take(30));
                return (Updated: updated, RuleCount: rules.Rules.Count, Diagnostics: diagnostics);
            });

            if (!string.IsNullOrWhiteSpace(validation.Diagnostics))
            {
                OutputBox.Text = "Save refused: parser diagnostics were reported.\r\n" +
                    validation.Diagnostics;
                StatusText.Text = "Save blocked by parser diagnostics";
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, validation.Updated,
                new System.Text.UTF8Encoding(false));
            OutputBox.Text = $"Saved separate source copy: {dialog.FileName}\r\n" +
                $"Changed KB sections: {changedSectionCount}\r\n" +
                $"Parsed rules: {validation.RuleCount:N0}\r\n" +
                "Original source and game files were not modified.";
            _edits.Clear();
            _hasStructuralEdits = false;
            SaveButton.IsEnabled = false;
            BuildButton.IsEnabled = true;
            StatusText.Text = "Saved separate source copy";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Save failed";
        }
        finally
        {
            if (_edits.Count > 0 || _hasStructuralEdits)
                SaveButton.IsEnabled = true;
        }
    }
    private static string NormalizeLineEndings(string text, string original)
    {
        string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" :
            original.Contains('\n') ? "\n" : Environment.NewLine;
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return newline == "\n" ? normalized : normalized.Replace("\n", newline);
    }
}
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StoryEditor2.Parsing;
using ICSharpCode.AvalonEdit;
using System.Collections.Generic;

namespace StoryEditor2.Wpf;

public sealed class RuleEditWindow : Window
{
    private sealed class ConditionItem
    {
        public string Text { get; set; }
        public bool Negated { get; set; }
        public RuleConditionConnector Connector { get; set; }
        public ConditionItem() { Text = ""; }
        public ConditionItem(RuleCondition item)
        {
            Text = item.Text;
            Negated = item.Negated;
            Connector = item.Connector;
        }
        // The list contains condition expressions only. Connectors belong to the
        // rule structure and are emitted by BuildRuleText(), not shown as list items.
        public override string ToString() => (Negated ? "NOT " : "") + Text;
    }

    private sealed class ActionItem
    {
        public string Text { get; set; }
        public bool Negated { get; set; }
        public ActionItem() { Text = ""; }
        public ActionItem(RuleAction item) { Text = item.Text; Negated = item.Negated; }
        public override string ToString() => (Negated ? "NOT " : "") + Text;
    }

    private readonly TextEditor _editor;
    private readonly ListBox _conditionList;
    private readonly ListBox _actionList;
    private readonly List<ConditionItem> _conditions;
    private readonly List<ActionItem> _actions;
    private readonly RuleKind _kind;
    private readonly string? _procedureSignature;
    private readonly string _originalText;
    private readonly RuleCondition[] _originalConditions;
    private readonly RuleAction[] _originalActions;
    private readonly int _ruleStart;

    public string ResultText { get; private set; } = "";

    public RuleEditWindow(string originalText, RuleKind kind,
        IReadOnlyList<RuleCondition> conditions, IReadOnlyList<RuleAction> actions,
        string? procedureSignature, int ruleStart)
    {
        _kind = kind;
        _procedureSignature = procedureSignature;
        _originalText = originalText;
        _ruleStart = ruleStart;
        _originalConditions = conditions.ToArray();
        _originalActions = actions.ToArray();
        _conditions = conditions.Select(c => new ConditionItem(c)).ToList();
        _actions = actions.Select(a => new ActionItem(a)).ToList();

        Title = kind == RuleKind.If ? "Edit IF rule — structure" : "Edit PROC rule — structure";
        Width = 1120;
        Height = 760;
        MinWidth = 780;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224));

        _editor = new TextEditor
        {
            Text = originalText, ShowLineNumbers = true, WordWrap = false,
            FontFamily = new FontFamily("Consolas"), FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212)),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(133, 133, 133)),
            Padding = new Thickness(8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var note = new TextBlock
        {
            Text = "Edit individual conditions and actions. The source preview is read-only; Validate & Apply rebuilds this rule and checks it in the main editor before changing the document.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var cancel = MakeButton("Cancel"); cancel.IsCancel = true;
        var apply = MakeButton("Validate & Apply"); apply.IsDefault = true;
        apply.Click += (_, _) =>
        {
            if (_conditions.Count == 0)
            {
                MessageBox.Show(this, "A rule must contain at least one condition.", "Invalid rule", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_actions.Count == 0)
            {
                MessageBox.Show(this, "A rule must contain at least one action.", "Invalid rule", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_conditions.Any(x => string.IsNullOrWhiteSpace(x.Text)) || _actions.Any(x => string.IsNullOrWhiteSpace(x.Text)))
            {
                MessageBox.Show(this, "Conditions and actions cannot be empty.", "Invalid rule", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            for (int i = 0; i < _conditions.Count; i++)
            {
                if (!HasBalancedParentheses(_conditions[i].Text))
                {
                    MessageBox.Show(this, $"Condition {i + 1} has unbalanced parentheses. The rule was not changed.", "Invalid condition", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            for (int i = 0; i < _actions.Count; i++)
            {
                if (!HasBalancedParentheses(_actions[i].Text))
                {
                    MessageBox.Show(this, $"Action {i + 1} has unbalanced parentheses. The rule was not changed.", "Invalid action", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            ResultText = BuildRuleText();
            DialogResult = true;
        };
        footer.Children.Add(cancel); footer.Children.Add(apply); root.Children.Add(footer);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var structure = new DockPanel();
        if (kind == RuleKind.Proc && !string.IsNullOrWhiteSpace(procedureSignature))
        {
            var signaturePanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(38, 48, 58)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(65, 85, 105)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 8),
                Child = new TextBlock { Text = "PROCEDURE: " + procedureSignature, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(190, 220, 245)), FontWeight = FontWeights.SemiBold }
            };
            DockPanel.SetDock(signaturePanel, Dock.Top); structure.Children.Add(signaturePanel);
        }

        _conditionList = MakeList();
        _actionList = MakeList();
        var conditionPanel = BuildListPanel("CONDITIONS", _conditionList,
            ("Add", () => AddCondition()), ("Edit", () => EditCondition()), ("Delete", () => DeleteCondition()),
            ("Up", () => MoveCondition(-1)), ("Down", () => MoveCondition(1)),
            ("NOT", () => ToggleConditionNot()), ("AND / AND NOT", () => ToggleConditionConnector()));
        var actionPanel = BuildListPanel("ACTIONS", _actionList,
            ("Add", () => AddAction()), ("Edit", () => EditAction()), ("Delete", () => DeleteAction()),
            ("Up", () => MoveAction(-1)), ("Down", () => MoveAction(1)),
            ("NOT", () => ToggleActionNot()));
        DockPanel.SetDock(actionPanel, Dock.Bottom);
        structure.Children.Add(actionPanel);
        structure.Children.Add(conditionPanel);

        Grid.SetColumn(structure, 0); body.Children.Add(structure);
        var divider = new Border { Background = new SolidColorBrush(Color.FromRgb(65, 65, 70)), Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(divider, 1); body.Children.Add(divider);
        Grid.SetColumn(_editor, 2); body.Children.Add(_editor);

        _conditionList.SelectionChanged += (_, _) => RefreshPreview();
        _actionList.SelectionChanged += (_, _) => RefreshPreview();
        root.Children.Add(body);
        Content = root;
        RefreshLists();
        Loaded += (_, _) => { _editor.IsReadOnly = true; _editor.Focus(); _editor.TextArea.Caret.Offset = 0; };
    }

    private static bool HasBalancedParentheses(string expression)
    {
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (c == '"' && (i == 0 || expression[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }
            if (inString) continue;
            if (c == '(') depth++;
            else if (c == ')' && --depth < 0) return false;
        }
        return !inString && depth == 0;
    }
    private string BuildRuleText()
    {
        bool conditionsChanged = !ConditionsMatchOriginal();
        bool actionsChanged = !ActionsMatchOriginal();
        if (!conditionsChanged && !actionsChanged)
            return _originalText;

        if (!TryGetOriginalSections(out int conditionStart, out int conditionEnd,
            out int actionStart, out int actionEnd))
            return BuildCanonicalRuleText();

        string newline = _originalText.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string prefix = _originalText[..conditionStart];
        string middle = _originalText[conditionEnd..actionStart];
        string suffix = _originalText[actionEnd..];

        string conditionBody = conditionsChanged
            ? BuildConditionBody(newline)
            : _originalText[conditionStart..conditionEnd];
        string actionBody = actionsChanged
            ? BuildActionBody(newline)
            : _originalText[actionStart..actionEnd];

        return prefix + conditionBody + middle + actionBody + suffix;
    }

    private bool ConditionsMatchOriginal()
    {
        if (_conditions.Count != _originalConditions.Length) return false;
        for (int i = 0; i < _conditions.Count; i++)
        {
            var current = _conditions[i];
            var original = _originalConditions[i];
            if (!string.Equals(current.Text, original.Text, StringComparison.Ordinal) ||
                current.Negated != original.Negated ||
                current.Connector != original.Connector)
                return false;
        }
        return true;
    }

    private bool ActionsMatchOriginal()
    {
        if (_actions.Count != _originalActions.Length) return false;
        for (int i = 0; i < _actions.Count; i++)
        {
            var current = _actions[i];
            var original = _originalActions[i];
            if (!string.Equals(current.Text, original.Text, StringComparison.Ordinal) ||
                current.Negated != original.Negated)
                return false;
        }
        return true;
    }

    private bool TryGetOriginalSections(out int conditionStart, out int conditionEnd,
        out int actionStart, out int actionEnd)
    {
        conditionStart = conditionEnd = actionStart = actionEnd = 0;
        if (_originalConditions.Length == 0 || _originalActions.Length == 0)
            return false;

        conditionStart = _originalConditions.Min(c => c.Start) - _ruleStart;
        conditionEnd = _originalConditions.Max(c => c.Start + c.Length) - _ruleStart;
        actionStart = _originalActions.Min(a => a.Start) - _ruleStart;
        actionEnd = _originalActions.Max(a => a.Start + a.Length) - _ruleStart;

        if (_kind == RuleKind.If && _originalConditions[0].Negated)
            conditionStart = FindLeadingKeyword(conditionStart, 0, "NOT", allowNot: false);
        else if (_kind == RuleKind.Proc)
            conditionStart = FindLeadingKeyword(conditionStart, 0, "AND", allowNot: true);

        if (_originalActions[0].Negated)
            actionStart = FindLeadingKeyword(actionStart, conditionEnd, "NOT", allowNot: false);

        return conditionStart >= 0 && conditionStart <= conditionEnd &&
            conditionEnd <= actionStart && actionStart <= actionEnd &&
            actionEnd <= _originalText.Length;
    }

    private int FindLeadingKeyword(int expressionStart, int lowerBound,
        string keyword, bool allowNot)
    {
        for (int i = expressionStart - keyword.Length; i >= lowerBound; i--)
        {
            if (!string.Equals(_originalText.Substring(i, keyword.Length), keyword,
                StringComparison.Ordinal))
                continue;
            if (i > 0 && !char.IsWhiteSpace(_originalText[i - 1]))
                continue;

            string between = _originalText[(i + keyword.Length)..expressionStart].Trim();
            if (between.Length == 0 || (allowNot && between == "NOT"))
                return i;
        }
        return expressionStart;
    }

    private string BuildConditionBody(string newline)
    {
        var lines = new List<string>();
        bool standaloneAnd = _kind == RuleKind.If && UsesStandaloneAndLines();

        for (int i = 0; i < _conditions.Count; i++)
        {
            var condition = _conditions[i];
            if (_kind == RuleKind.Proc)
            {
                lines.Add("AND");
                bool negated = condition.Connector == RuleConditionConnector.AndNot ||
                    (i == 0 && condition.Negated);
                lines.Add((negated ? "NOT " : "") + condition.Text.Trim());
            }
            else if (i > 0 && standaloneAnd)
            {
                lines.Add("AND");
                if (condition.Connector == RuleConditionConnector.AndNot)
                    lines.Add("NOT " + condition.Text.Trim());
                else
                    lines.Add(condition.Text.Trim());
            }
            else
            {
                string prefix = i == 0
                    ? (condition.Negated ? "NOT " : "")
                    : condition.Connector == RuleConditionConnector.AndNot
                        ? "AND NOT " : "AND ";
                lines.Add(prefix + condition.Text.Trim());
            }
        }
        return string.Join(newline, lines);
    }

    private bool UsesStandaloneAndLines()
    {
        int position = 0;
        while ((position = _originalText.IndexOf("AND", position, StringComparison.Ordinal)) >= 0)
        {
            bool leftBoundary = position == 0 || !char.IsLetterOrDigit(_originalText[position - 1]) &&
                _originalText[position - 1] != '_';
            int after = position + 3;
            bool rightBoundary = after >= _originalText.Length ||
                !char.IsLetterOrDigit(_originalText[after]) && _originalText[after] != '_';
            if (leftBoundary && rightBoundary)
            {
                while (after < _originalText.Length &&
                    (_originalText[after] == ' ' || _originalText[after] == '\t'))
                    after++;
                if (after < _originalText.Length &&
                    (_originalText[after] == '\r' || _originalText[after] == '\n'))
                    return true;
            }
            position += 3;
        }
        return false;
    }

    private string BuildActionBody(string newline)
    {
        var lines = new List<string>();
        for (int i = 0; i < _actions.Count; i++)
        {
            var action = _actions[i];
            lines.Add((action.Negated ? "NOT " : "") + action.Text.Trim() +
                (i < _actions.Count - 1 ? ";" : ""));
        }
        return string.Join(newline, lines);
    }

    private string BuildCanonicalRuleText()
    {
        var lines = new List<string>();
        if (_kind == RuleKind.If)
        {
            lines.Add("IF");
            for (int i = 0; i < _conditions.Count; i++)
            {
                var c = _conditions[i];
                string prefix = i == 0 ? (c.Negated ? "NOT " : "") :
                    c.Connector == RuleConditionConnector.AndNot ? "AND NOT " : "AND ";
                lines.Add(prefix + c.Text.Trim());
            }
        }
        else
        {
            lines.Add("PROC");
            lines.Add(_procedureSignature ?? throw new InvalidOperationException("PROC signature is missing."));
            for (int i = 0; i < _conditions.Count; i++)
            {
                var c = _conditions[i];
                lines.Add("AND");
                lines.Add((c.Connector == RuleConditionConnector.AndNot ||
                    (i == 0 && c.Negated) ? "NOT " : "") + c.Text.Trim());
            }
        }
        lines.Add("THEN");
        foreach (var a in _actions)
            lines.Add((a.Negated ? "NOT " : "") + a.Text.Trim() +
                (a.Text.TrimEnd().EndsWith(';') ? "" : ";"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private void RefreshLists()
    {
        _conditionList.ItemsSource = null; _conditionList.ItemsSource = _conditions;
        _actionList.ItemsSource = null; _actionList.ItemsSource = _actions;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        _editor.Text = BuildRuleText();

        if (_conditionList.SelectedIndex >= 0)
        {
            int selectedIndex = _conditionList.SelectedIndex;
            int searchFrom = 0;
            for (int i = 0; i <= selectedIndex; i++)
            {
                string expression = _conditions[i].Text.Trim();
                int found = _editor.Text.IndexOf(expression, searchFrom, StringComparison.Ordinal);
                if (found < 0) return;
                searchFrom = found + expression.Length;
                if (i == selectedIndex)
                {
                    _editor.Select(found, expression.Length);
                    _editor.ScrollToLine(_editor.Document.GetLineByOffset(found).LineNumber);
                }
            }
        }
        else if (_actionList.SelectedIndex >= 0)
        {
            int selectedIndex = _actionList.SelectedIndex;
            int thenStart = _editor.Text.IndexOf("THEN", StringComparison.Ordinal);
            int searchFrom = thenStart < 0 ? 0 : thenStart + 4;
            for (int i = 0; i <= selectedIndex; i++)
            {
                string expression = _actions[i].Text.Trim();
                int found = _editor.Text.IndexOf(expression, searchFrom, StringComparison.Ordinal);
                if (found < 0) return;
                searchFrom = found + expression.Length;
                if (i == selectedIndex)
                {
                    _editor.Select(found, expression.Length);
                    _editor.ScrollToLine(_editor.Document.GetLineByOffset(found).LineNumber);
                }
            }
        }
        else
        {
            _editor.Select(0, 0);
        }
    }

    private void AddCondition()
    {
        string? text = AskText("Add condition", "Enter an Osiris condition:");
        if (string.IsNullOrWhiteSpace(text)) return;
        _conditions.Add(new ConditionItem { Text = text.Trim(), Negated = false, Connector = _conditions.Count == 0 ? RuleConditionConnector.First : RuleConditionConnector.And });
        RefreshLists(); _conditionList.SelectedIndex = _conditions.Count - 1;
    }
    private void EditCondition()
    {
        int i = _conditionList.SelectedIndex; if (i < 0) return;
        string? text = AskText("Edit condition", "Condition expression:", _conditions[i].Text);
        if (string.IsNullOrWhiteSpace(text)) return;
        _conditions[i].Text = text.Trim(); RefreshLists(); _conditionList.SelectedIndex = i;
    }
    private void DeleteCondition()
    {
        int i = _conditionList.SelectedIndex; if (i < 0) return;
        _conditions.RemoveAt(i);
        if (_conditions.Count > 0) _conditions[0].Connector = RuleConditionConnector.First;
        RefreshLists(); if (_conditions.Count > 0) _conditionList.SelectedIndex = Math.Min(i, _conditions.Count - 1);
    }
    private void MoveCondition(int delta)
    {
        int i = _conditionList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _conditions.Count) return;
        (_conditions[i].Text, _conditions[j].Text) = (_conditions[j].Text, _conditions[i].Text);
        (_conditions[i].Negated, _conditions[j].Negated) = (_conditions[j].Negated, _conditions[i].Negated);
        RefreshLists(); _conditionList.SelectedIndex = j;
    }
    private void ToggleConditionNot()
    {
        int i = _conditionList.SelectedIndex; if (i < 0) return;
        _conditions[i].Negated = !_conditions[i].Negated;
        RefreshLists(); _conditionList.SelectedIndex = i;
    }
    private void ToggleConditionConnector()
    {
        int i = _conditionList.SelectedIndex; if (i <= 0) return;
        _conditions[i].Connector = _conditions[i].Connector == RuleConditionConnector.AndNot
            ? RuleConditionConnector.And : RuleConditionConnector.AndNot;
        RefreshLists(); _conditionList.SelectedIndex = i;
    }    private void AddAction()
    {
        string? text = AskText("Add action", "Enter an Osiris action:");
        if (string.IsNullOrWhiteSpace(text)) return;
        _actions.Add(new ActionItem { Text = text.Trim().TrimEnd(';'), Negated = false });
        RefreshLists(); _actionList.SelectedIndex = _actions.Count - 1;
    }
    private void EditAction()
    {
        int i = _actionList.SelectedIndex; if (i < 0) return;
        string? text = AskText("Edit action", "Action expression:", _actions[i].Text);
        if (string.IsNullOrWhiteSpace(text)) return;
        _actions[i].Text = text.Trim().TrimEnd(';'); RefreshLists(); _actionList.SelectedIndex = i;
    }
    private void ToggleActionNot()
    {
        int i = _actionList.SelectedIndex; if (i < 0) return;
        _actions[i].Negated = !_actions[i].Negated;
        RefreshLists(); _actionList.SelectedIndex = i;
    }    private void DeleteAction()
    {
        int i = _actionList.SelectedIndex; if (i < 0) return;
        _actions.RemoveAt(i); RefreshLists(); if (_actions.Count > 0) _actionList.SelectedIndex = Math.Min(i, _actions.Count - 1);
    }
    private void MoveAction(int delta)
    {
        int i = _actionList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _actions.Count) return;
        (_actions[i], _actions[j]) = (_actions[j], _actions[i]);
        RefreshLists(); _actionList.SelectedIndex = j;
    }

    private static string? AskText(string title, string prompt, string initial = "")
    {
        var dialog = new Window { Title = title, Width = 560, Height = 190, MinWidth = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize,
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 38)), Foreground = Brushes.White };
        var panel = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = MakeButton("OK"); ok.IsDefault = true;
        var cancel = MakeButton("Cancel"); cancel.IsCancel = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons);
        var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), Foreground = Brushes.White };
        DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
        var input = new TextBox { Text = initial, AcceptsReturn = false, Padding = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(25, 25, 28)), Foreground = Brushes.White };
        panel.Children.Add(input); dialog.Content = panel;
        string? result = null;
        ok.Click += (_, _) => { result = input.Text; dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? result : null;
    }

    private static DockPanel BuildListPanel(string title, ListBox list, params (string Label, Action Run)[] actions)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 4, 4, 6), Foreground = Brushes.Gainsboro };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var item in actions)
        {
            var button = MakeButton(item.Label); button.MinWidth = 55; button.Padding = new Thickness(7, 4, 7, 4);
            button.Click += (_, _) => item.Run(); buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(list);
        return panel;
    }

    private static ListBox MakeList() => new()
    {
        MinHeight = 100, Background = new SolidColorBrush(Color.FromRgb(38, 38, 42)),
        Foreground = Brushes.Gainsboro, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 6)
    };

    private static Button MakeButton(string label) => new()
    {
        Content = label, MinWidth = 90, Margin = new Thickness(4, 0, 0, 0),
        Padding = new Thickness(10, 5, 10, 5),
        Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
        Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 90))
    };
}
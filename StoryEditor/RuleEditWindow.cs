using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using StoryEditor2.Parsing;

namespace StoryEditor2.Wpf;

public sealed class RuleEditWindow : Window
{
    private readonly TextEditor _editor;
    private readonly OsirisColorizer _colorizer = new();
    private readonly BracketPairColorizer _bracketColorizer = new();

    public string ResultText { get; private set; } = "";

    public RuleEditWindow(
        string originalText,
        RuleKind kind,
        IReadOnlyList<RuleCondition> conditions,
        IReadOnlyList<RuleAction> actions,
        string? procedureSignature,
        int ruleStart,
        ApiSymbolCatalog? apiSymbols = null,
        Func<IEnumerable<OsirisCompletionSymbol>>? getProcedureSymbols = null)
    {
        Title = kind == RuleKind.If ? "Edit IF rule" : "Edit PROC rule";
        Width = 1120;
        Height = 760;
        MinWidth = 680;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224));

        _editor = new TextEditor
        {
            Text = originalText,
            IsReadOnly = false,
            ShowLineNumbers = true,
            WordWrap = false,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212)),
            LineNumbersForeground = new SolidColorBrush(Color.FromRgb(133, 133, 133)),
            Padding = new Thickness(8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        _editor.TextArea.TextView.LineTransformers.Add(_bracketColorizer);
        _colorizer.Update(_editor.Text, apiSymbols);
        _bracketColorizer.Update(_editor.Text, _editor.CaretOffset);
        _editor.TextChanged += (_, _) =>
        {
            _colorizer.Update(_editor.Text, apiSymbols);
            _bracketColorizer.Update(_editor.Text, _editor.CaretOffset);
            _editor.TextArea.TextView.Redraw();
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            _bracketColorizer.Update(_editor.Text, _editor.CaretOffset);
            _editor.TextArea.TextView.Redraw();
        };

        OsirisEditorAssist.Attach(_editor, () =>
        {
            if (apiSymbols is null)
                return Array.Empty<OsirisCompletionSymbol>();

            return apiSymbols.Functions.Select(pair =>
            {
                string signature = apiSymbols.FunctionSignatures.TryGetValue(pair.Key, out var apiSignature)
                    ? "(" + string.Join(", ", apiSignature.Parameters.Select(parameter =>
                        $"[{parameter.Direction}] {parameter.Type}")) + ")"
                    : "()";
                return new OsirisCompletionSymbol(
                    pair.Key,
                    pair.Value.ToString(),
                    signature,
                    "Declared in the Osiris API catalog.");
            });
        }, getProcedureSymbols, allowRuleSyntax: true);

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var help = new TextBlock
        {
            Text = "Edit the rule source directly. You can change conditions, actions, connectors, comments and formatting. The edited text will be parsed and checked before it replaces the original rule.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180))
        };
        header.Children.Add(help);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var hint = new TextBlock
        {
            Text = "Ctrl+Enter — Apply    Esc — Cancel",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(145, 145, 145))
        };
        DockPanel.SetDock(hint, Dock.Left);
        footer.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = MakeButton("Cancel");
        cancel.IsCancel = true;
        var apply = MakeButton("Validate & Apply");
        apply.IsDefault = true;
        apply.Click += (_, _) => ApplyChanges();
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);

        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_editor);
        Content = root;

        _editor.TextArea.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ApplyChanges();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                InsertIndentedNewLine();
                e.Handled = true;
            }
        };

        Loaded += (_, _) =>
        {
            _editor.Focus();
            _editor.TextArea.Caret.Offset = 0;
            _editor.ScrollToHome();
        };
    }

    private void InsertIndentedNewLine()
    {
        var document = _editor.Document;
        int caretOffset = _editor.CaretOffset;
        var line = document.GetLineByOffset(caretOffset);
        string beforeCaret = document.GetText(line.Offset, caretOffset - line.Offset);
        string indentation = new(beforeCaret.TakeWhile(c => c is ' ' or '\t').ToArray());
        string trimmed = beforeCaret.TrimEnd();

        // Osiris actions following THEN are visually nested one level deeper.
        // Existing indentation is otherwise preserved exactly as typed.
        if (string.Equals(trimmed.Trim(), "THEN", StringComparison.OrdinalIgnoreCase))
        {
            indentation += "    ";
        }
        else if (trimmed.EndsWith('('))
        {
            indentation += "    ";
        }

        string insertion = Environment.NewLine + indentation;
        int replaceStart = _editor.SelectionStart;
        int replaceLength = _editor.SelectionLength;
        document.Replace(replaceStart, replaceLength, insertion);
        _editor.CaretOffset = replaceStart + insertion.Length;
    }

    private void ApplyChanges()
    {
        if (string.IsNullOrWhiteSpace(_editor.Text))
        {
            MessageBox.Show(this, "The rule cannot be empty.", "Invalid rule",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultText = _editor.Text;
        DialogResult = true;
    }

    private static Button MakeButton(string label) => new()
    {
        Content = label,
        MinWidth = 110,
        Margin = new Thickness(6, 0, 0, 0),
        Padding = new Thickness(10, 5, 10, 5),
        Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 90))
    };
}

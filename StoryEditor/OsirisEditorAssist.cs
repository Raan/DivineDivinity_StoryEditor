using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace StoryEditor2.Wpf;

public sealed record OsirisCompletionSymbol(
    string Name,
    string Category,
    string Signature,
    string Description);

internal sealed class OsirisEditorAssist
{
    private readonly TextEditor _editor;
    private readonly Func<IEnumerable<OsirisCompletionSymbol>> _getApiSymbols;
    private readonly Func<IEnumerable<OsirisCompletionSymbol>> _getProcedureSymbols;
    private readonly bool _lifecycleActions;
    private CompletionWindow? _completionWindow;

    private const string DarkScrollBarStyleXaml = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="{x:Type ScrollBar}">
          <Setter Property="Width" Value="12"/>
          <Setter Property="Background" Value="#FF252525"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="{x:Type ScrollBar}">
                <Grid Background="#FF252525">
                  <Track x:Name="PART_Track" IsDirectionReversed="True">
                    <Track.Thumb>
                      <Thumb>
                        <Thumb.Template>
                          <ControlTemplate TargetType="{x:Type Thumb}">
                            <Border Background="#FF66666D" CornerRadius="4" Margin="2"/>
                          </ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    private OsirisEditorAssist(
        TextEditor editor,
        Func<IEnumerable<OsirisCompletionSymbol>> getApiSymbols,
        Func<IEnumerable<OsirisCompletionSymbol>> getProcedureSymbols,
        bool allowRuleSyntax,
        bool lifecycleActions)
    {
        _editor = editor;
        _getApiSymbols = getApiSymbols;
        _getProcedureSymbols = getProcedureSymbols;
        _lifecycleActions = lifecycleActions;

        _editor.TextArea.TextEntered += TextArea_TextEntered;
        _editor.TextArea.TextEntering += TextArea_TextEntering;
        _editor.ContextMenu = CreateContextMenu(allowRuleSyntax, lifecycleActions);
    }

    public static OsirisEditorAssist Attach(
        TextEditor editor,
        Func<IEnumerable<OsirisCompletionSymbol>> getApiSymbols,
        Func<IEnumerable<OsirisCompletionSymbol>>? getProcedureSymbols = null,
        bool allowRuleSyntax = false,
        bool lifecycleActions = false)
    {
        return new OsirisEditorAssist(
            editor,
            getApiSymbols,
            getProcedureSymbols ?? (() => Array.Empty<OsirisCompletionSymbol>()),
            allowRuleSyntax,
            lifecycleActions);
    }

    private ContextMenu CreateContextMenu(bool allowRuleSyntax, bool lifecycleActions)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromArgb(184, 30, 30, 30)),
            Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, 90, 90, 96)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            SnapsToDevicePixels = true,
            HasDropShadow = true
        };

        // A custom MenuItem template removes the bright system icon gutter
        // and gives nested menus the same translucent appearance.
        const string menuItemStyleXaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type MenuItem}">
              <Setter Property="Foreground" Value="#E0E0E0"/>
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Padding" Value="10,6"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="{x:Type MenuItem}">
                    <Grid>
                      <Border x:Name="ItemBorder" Background="{TemplateBinding Background}">
                        <Grid>
                          <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="18"/>
                          </Grid.ColumnDefinitions>
                          <ContentPresenter ContentSource="Header" Margin="2,0,8,0"
                                            RecognizesAccessKey="True" VerticalAlignment="Center"/>
                          <Path x:Name="Arrow" Grid.Column="1" Data="M 0,0 L 4,4 L 0,8 Z"
                                Fill="#C8C8C8" HorizontalAlignment="Center"
                                VerticalAlignment="Center" Visibility="Collapsed"/>
                        </Grid>
                      </Border>
                      <Popup x:Name="PART_Popup" Placement="Right"
                             PlacementTarget="{Binding RelativeSource={RelativeSource TemplatedParent}}"
                             AllowsTransparency="True" Focusable="False"
                             IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                        <Border Background="#B81E1E1E" BorderBrush="#D25A5A60"
                                BorderThickness="1" Padding="3">
                          <ScrollViewer CanContentScroll="True"
                                        VerticalScrollBarVisibility="Auto"
                                        HorizontalScrollBarVisibility="Disabled">
                            <ItemsPresenter/>
                          </ScrollViewer>
                        </Border>
                      </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property="HasItems" Value="True">
                        <Setter TargetName="Arrow" Property="Visibility" Value="Visible"/>
                      </Trigger>
                      <Trigger Property="IsHighlighted" Value="True">
                        <Setter TargetName="ItemBorder" Property="Background" Value="#965F7D9B"/>
                      </Trigger>
                      <Trigger Property="IsSubmenuOpen" Value="True">
                        <Setter TargetName="ItemBorder" Property="Background" Value="#965F7D9B"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
        menu.Resources.Add(typeof(MenuItem), (Style)XamlReader.Parse(menuItemStyleXaml));

        // Replace the default ContextMenu template as well: the stock template
        // reserves a separate icon gutter that can remain bright on some themes.
        const string contextMenuTemplateXaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type ContextMenu}">
              <Border Background="{TemplateBinding Background}"
                      BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="{TemplateBinding BorderThickness}"
                      Padding="{TemplateBinding Padding}"
                      SnapsToDevicePixels="True">
                <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
              </Border>
            </ControlTemplate>
            """;
        menu.Template = (ControlTemplate)XamlReader.Parse(contextMenuTemplateXaml);

        var separatorStyle = new Style(typeof(Separator));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,
            new Thickness(8, 3, 8, 3)));
        separatorStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1.0));
        separatorStyle.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(120, 160, 160, 165))));
        menu.Resources.Add(typeof(Separator), separatorStyle);

        menu.Resources.Add(typeof(ScrollBar),
            (Style)XamlReader.Parse(DarkScrollBarStyleXaml));

        if (lifecycleActions)
        {
            var insertAction = new MenuItem { Header = "Insert API action..." };
            insertAction.Click += (_, _) => ShowCompletion(
                _getApiSymbols().Where(symbol =>
                    symbol.Category.Equals("Call", StringComparison.OrdinalIgnoreCase) ||
                    symbol.Category.Equals("SystemCall", StringComparison.OrdinalIgnoreCase)),
                appendSemicolon: true,
                startOnNewLine: true);
            menu.Items.Add(insertAction);

            var insertProcedure = new MenuItem { Header = "Insert Story procedure..." };
            insertProcedure.Click += (_, _) =>
            {
                var procedures = _getProcedureSymbols().ToArray();
                if (procedures.Length > 0)
                    ShowCompletion(procedures, appendSemicolon: true, startOnNewLine: true);
                else
                    MessageBox.Show(Window.GetWindow(_editor),
                        "No Story procedures were found in the loaded project.",
                        "Insert Story procedure", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            menu.Items.Add(insertProcedure);

            var builtins = new MenuItem { Header = "Insert built-in action" };
            AddLifecycleAction(builtins, "GoalCompleted;");
            var goalControl = new MenuItem { Header = "Goal control directive (!)" };
            goalControl.Click += (_, _) => InsertLifecycleText("!", 1);
            builtins.Items.Add(goalControl);
            var negated = new MenuItem { Header = "NOT action prefix" };
            negated.Click += (_, _) => InsertLifecycleText("NOT ");
            builtins.Items.Add(negated);
            menu.Items.Add(builtins);
        }
        else
        {
            var insertFunction = new MenuItem { Header = "Insert API function..." };
            insertFunction.Click += (_, _) => ShowCompletion(_getApiSymbols());
            menu.Items.Add(insertFunction);
        }

        if (allowRuleSyntax)
        {
            var insertProcedure = new MenuItem { Header = "Insert Story procedure..." };
            insertProcedure.Click += (_, _) =>
            {
                var procedures = _getProcedureSymbols().ToArray();
                if (procedures.Length == 0)
                {
                    MessageBox.Show(
                        Window.GetWindow(_editor),
                        "No Story procedures were found in the loaded project.",
                        "Insert Story procedure",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                ShowCompletion(procedures);
            };

            var insertKeyword = new MenuItem { Header = "Insert Osiris keyword" };
            AddKeyword(insertKeyword, "AND");
            AddKeyword(insertKeyword, "OR");
            AddKeyword(insertKeyword, "NOT");
            AddKeyword(insertKeyword, "THEN");

            var insertIfTemplate = new MenuItem { Header = "Insert IF rule template" };
            insertIfTemplate.Click += (_, _) =>
            {
                const string template = "IF" + "\r\n    " + "\r\nTHEN" + "\r\n    ";
                InsertText(template, template.IndexOf("    ", StringComparison.Ordinal) + 4);
            };

            menu.Items.Add(insertProcedure);
            menu.Items.Add(new Separator());
            menu.Items.Add(insertKeyword);
            menu.Items.Add(insertIfTemplate);
        }
        return menu;

        void AddKeyword(MenuItem parent, string keyword)
        {
            var item = new MenuItem { Header = keyword };
            item.Click += (_, _) => InsertText(keyword + " ");
            parent.Items.Add(item);
        }

        void AddLifecycleAction(MenuItem parent, string source)
        {
            var item = new MenuItem { Header = source };
            item.Click += (_, _) => InsertLifecycleText(source);
            parent.Items.Add(item);
        }
    }

    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (_completionWindow is not null ||
            e.Text.Length == 0 ||
            !(char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_'))
            return;

        int start = GetIdentifierStart(_editor.CaretOffset);
        int length = _editor.CaretOffset - start;
        if (length < 2)
            return;

        string prefix = _editor.Document.GetText(start, length);
        var apiSymbols = _getApiSymbols();
        if (_lifecycleActions)
            apiSymbols = apiSymbols.Where(symbol =>
                symbol.Category.Equals("Call", StringComparison.OrdinalIgnoreCase) ||
                symbol.Category.Equals("SystemCall", StringComparison.OrdinalIgnoreCase));
        var matches = apiSymbols
            .Concat(_lifecycleActions ? Array.Empty<OsirisCompletionSymbol>() : _getProcedureSymbols())
            .GroupBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(symbol => symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return;

        ShowCompletion(matches, start);
    }

    private void TextArea_TextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (_completionWindow is null || e.Text != "(")
            return;

        // The completion item inserts the matching pair itself.
        e.Handled = true;
        _completionWindow.CompletionList.RequestInsertion(e);
    }

    private int GetIdentifierStart(int caretOffset)
    {
        int start = caretOffset;
        while (start > 0)
        {
            char c = _editor.Document.GetCharAt(start - 1);
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                break;
            start--;
        }
        return start;
    }

    private void ShowCompletion(IEnumerable<OsirisCompletionSymbol> symbols,
        int? startOffset = null, bool appendSemicolon = false, bool startOnNewLine = true)
    {
        _completionWindow?.Close();

        var items = symbols
            .GroupBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (items.Length == 0)
            return;

        var window = new CompletionWindow(_editor.TextArea);
        _completionWindow = window;
        window.StartOffset = startOffset ?? _editor.CaretOffset;
        window.EndOffset = _editor.CaretOffset;

        var popupBackground = new SolidColorBrush(Color.FromArgb(184, 30, 30, 30));
        var popupForeground = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        window.Background = popupBackground;
        window.Foreground = popupForeground;
        window.AllowsTransparency = true;
        window.MaxHeight = Math.Max(180, SystemParameters.WorkArea.Height * 0.72);
        window.MaxWidth = Math.Max(480, SystemParameters.WorkArea.Width * 0.82);
        window.MinWidth = Math.Min(600, SystemParameters.WorkArea.Width * 0.82);
        window.Width = Math.Min(720, SystemParameters.WorkArea.Width * 0.82);

        var completionList = window.CompletionList.ListBox;
        completionList.Background = popupBackground;
        completionList.Foreground = popupForeground;
        completionList.BorderThickness = new Thickness(0);
        completionList.Padding = new Thickness(2);
        completionList.MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height * 0.68);
        ScrollViewer.SetVerticalScrollBarVisibility(completionList, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(completionList, ScrollBarVisibility.Disabled);
        completionList.Resources.Add(typeof(ScrollBar),
            (Style)XamlReader.Parse(DarkScrollBarStyleXaml));

        var completionItemStyle = new Style(typeof(ListBoxItem));
        completionItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, popupForeground));
        completionItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        completionItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 3, 7, 3)));
        completionItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        completionItemStyle.Triggers.Add(new Trigger
        {
            Property = ListBoxItem.IsSelectedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty,
                    new SolidColorBrush(Color.FromArgb(190, 65, 95, 125))),
                new Setter(Control.ForegroundProperty, Brushes.White)
            }
        });
        completionItemStyle.Triggers.Add(new Trigger
        {
            Property = ListBoxItem.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty,
                    new SolidColorBrush(Color.FromArgb(110, 65, 95, 125)))
            }
        });
        completionList.ItemContainerStyle = completionItemStyle;

        foreach (var symbol in items)
        {
            window.CompletionList.CompletionData.Add(
                new OsirisCompletionData(symbol, appendSemicolon || _lifecycleActions, startOnNewLine));
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, window))
                _completionWindow = null;
        };
        window.Show();
    }

    private void InsertText(string text, int? caretInInsertion = null)
    {
        int start = _editor.SelectionStart;
        int length = _editor.SelectionLength;
        _editor.Document.Replace(start, length, text);
        _editor.CaretOffset = start + (caretInInsertion ?? text.Length);
        _editor.Focus();
    }

    private void InsertLifecycleText(string text, int? caretInInsertion = null)
    {
        int start = _editor.SelectionStart;
        int length = _editor.SelectionLength;
        string prefix = GetNewLinePrefix(start);
        _editor.Document.Replace(start, length, prefix + text);
        _editor.CaretOffset = start + prefix.Length + (caretInInsertion ?? text.Length);
        _editor.Focus();
    }

    private string GetNewLinePrefix(int offset)
    {
        var document = _editor.Document;
        var line = document.GetLineByOffset(Math.Clamp(offset, 0, document.TextLength));
        string beforeCaret = document.GetText(line.Offset, Math.Clamp(offset - line.Offset, 0, line.Length));
        if (beforeCaret.All(char.IsWhiteSpace))
            return string.Empty;

        string lineText = document.GetText(line);
        int indentLength = 0;
        while (indentLength < lineText.Length && char.IsWhiteSpace(lineText[indentLength]))
            indentLength++;

        string newline = document.Text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : document.Text.Contains("\n", StringComparison.Ordinal) ? "\n" : Environment.NewLine;
        return newline + lineText[..indentLength];
    }

    private sealed class OsirisCompletionData : ICompletionData
    {
        private readonly OsirisCompletionSymbol _symbol;
        private readonly bool _appendSemicolon;
        private readonly bool _startOnNewLine;

        public OsirisCompletionData(OsirisCompletionSymbol symbol, bool appendSemicolon, bool startOnNewLine)
        {
            _symbol = symbol;
            _appendSemicolon = appendSemicolon;
            _startOnNewLine = startOnNewLine;
        }

        public ImageSource Image => null!;
        public string Text => _symbol.Name;
        public object Content => _symbol.Name + "    " + _symbol.Signature;
        public object Description =>
            _symbol.Category + Environment.NewLine + _symbol.Description;
        public double Priority => 0;

        public void Complete(
            TextArea textArea,
            ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            // Capture the numeric offset before changing the document.
            // The completion segment may become invalid after Replace(),
            // especially when completion is inserted at the end of the file.
            var document = textArea.Document;
            int insertionOffset = Math.Clamp(completionSegment.Offset, 0, document.TextLength);
            int replaceLength = Math.Clamp(
                completionSegment.Length,
                0,
                document.TextLength - insertionOffset);

            string prefix = _startOnNewLine
                ? BuildNewLinePrefix(document, insertionOffset)
                : string.Empty;
            string insertion = prefix + _symbol.Name + "()" + (_appendSemicolon ? ";" : "");
            document.Replace(insertionOffset, replaceLength, insertion);

            int caretOffset = insertionOffset + prefix.Length + _symbol.Name.Length + 1;
            textArea.Caret.Offset = Math.Clamp(caretOffset, 0, document.TextLength);
        }

        private static string BuildNewLinePrefix(TextDocument document, int offset)
        {
            var line = document.GetLineByOffset(Math.Clamp(offset, 0, document.TextLength));
            string beforeCaret = document.GetText(line.Offset, Math.Clamp(offset - line.Offset, 0, line.Length));
            if (beforeCaret.All(char.IsWhiteSpace))
                return string.Empty;

            string lineText = document.GetText(line);
            int indentLength = 0;
            while (indentLength < lineText.Length && char.IsWhiteSpace(lineText[indentLength]))
                indentLength++;

            string newline = document.Text.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : document.Text.Contains("\n", StringComparison.Ordinal) ? "\n" : Environment.NewLine;
            return newline + lineText[..indentLength];
        }
    }
}

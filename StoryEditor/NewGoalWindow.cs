using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StoryEditor2.Parsing;

namespace StoryEditor2.Wpf;

internal sealed class NewGoalWindow : Window
{
    private readonly TextBox _titleBox;
    private readonly TextBlock _idLabel;
    private readonly ComboBox _parentBox;
    private readonly Func<string, int> _getGoalId;

    public string GoalTitle => _titleBox.Text.Trim();
    public int? ParentGoalId => (_parentBox.SelectedItem as ComboBoxItem)?.Tag as int?;

    public NewGoalWindow(Func<string, int> getGoalId, IReadOnlyList<GoalSymbol> goals)
    {
        _getGoalId = getGoalId;

        Title = "Create Goal";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(37, 37, 38));
        Foreground = Brushes.White;
        Padding = new Thickness(18);

        var panel = new StackPanel();
        _idLabel = new TextBlock { Foreground = Brushes.LightGray };
        panel.Children.Add(_idLabel);
        panel.Children.Add(new TextBlock
        {
            Text = "Goal title",
            Margin = new Thickness(0, 12, 0, 4)
        });

        _titleBox = new TextBox
        {
            Text = "New Goal",
            MinWidth = 350,
            Padding = new Thickness(6)
        };
        _titleBox.TextChanged += (_, _) => UpdateIdPreview();
        panel.Children.Add(_titleBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Parent Goal (optional)",
            Margin = new Thickness(0, 12, 0, 4)
        });
        _parentBox = new ComboBox
        {
            MinWidth = 350,
            Padding = new Thickness(5),
            IsEditable = false
        };
        _parentBox.Items.Add(new ComboBoxItem { Content = "(No parent)", Tag = null });
        foreach (var goal in goals.OrderBy(g => g.Id))
        {
            _parentBox.Items.Add(new ComboBoxItem
            {
                Content = $"Goal {goal.Id} — {goal.Title ?? "(untitled)"}",
                Tag = goal.Id
            });
        }
        _parentBox.SelectedIndex = 0;
        panel.Children.Add(_parentBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var create = new Button { Content = "Create", MinWidth = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        create.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            UpdateIdPreview();
            _titleBox.Focus();
            _titleBox.SelectAll();
        };
    }

    private void UpdateIdPreview()
    {
        _idLabel.Text = $"New Goal ID: {_getGoalId(_titleBox.Text.Trim())}";
    }
}

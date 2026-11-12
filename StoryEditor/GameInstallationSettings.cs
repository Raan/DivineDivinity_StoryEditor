using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace StoryEditor2.Wpf;

internal sealed record GameInstallationSettings(
    string GameRoot,
    string OsirisDllPath,
    string BaseStoryPath)
{
    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoryEditor");

    private static string LegacySettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StoryEditor2", "game-installation.json");

    private static string SettingsFile => Path.Combine(SettingsDirectory, "game-installation.json");

    public override string ToString() => GameRoot;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(GameRoot) || !Directory.Exists(GameRoot))
        {
            error = "The selected game folder does not exist.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(OsirisDllPath) || !File.Exists(OsirisDllPath) ||
            !string.Equals(Path.GetFileName(OsirisDllPath), "OsirisDLL.dll", StringComparison.OrdinalIgnoreCase))
        {
            error = "OsirisDLL.dll was not found at the saved path.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(BaseStoryPath) || !File.Exists(BaseStoryPath))
        {
            error = "The base story.000 file was not found at the saved path.";
            return false;
        }
        error = "";
        return true;
    }

    public static GameInstallationSettings? Load()
    {
        try
        {
            string sourcePath = File.Exists(SettingsFile) ? SettingsFile : LegacySettingsFile;
            if (!File.Exists(sourcePath)) return null;

            var settings = JsonSerializer.Deserialize<GameInstallationSettings>(
                File.ReadAllText(sourcePath, Encoding.UTF8));

            // Migrate settings from the previous StoryEditor2 product folder.
            if (settings is not null && sourcePath != SettingsFile)
                settings.Save();

            return settings;
        }
        catch { return null; }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        string temporary = SettingsFile + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, SettingsFile, true);
    }

    private static IReadOnlyList<GameInstallationSettings> DiscoverInstallations()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
                if (Directory.Exists(full)) roots.Add(full);
            }
            catch { }
        }

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (string basePath in new[] { pf, pf86 }.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            AddRoot(Path.Combine(basePath, "GOG Games", "Divine Divinity"));
            AddRoot(Path.Combine(basePath, "GOG Galaxy", "Games", "Divine Divinity"));
            AddRoot(Path.Combine(basePath, "Steam", "steamapps", "common", "Divine Divinity"));
        }

        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = key.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is not null)
                {
                    foreach (string subName in uninstall.GetSubKeyNames())
                    {
                        using var app = uninstall.OpenSubKey(subName);
                        string? displayName = app?.GetValue("DisplayName") as string;
                        if (displayName?.Contains("Divine Divinity", StringComparison.OrdinalIgnoreCase) == true)
                            AddRoot(app?.GetValue("InstallLocation") as string);
                    }
                }

                using var steam = key.OpenSubKey(@"SOFTWARE\Valve\Steam");
                string? steamPath = steam?.GetValue("SteamPath") as string ??
                    steam?.GetValue("InstallPath") as string;
                AddSteamLibraries(steamPath, AddRoot);
            }
            catch { }
        }

        var found = new List<GameInstallationSettings>();
        foreach (string root in roots)
        {
            string dll = Path.Combine(root, "OsirisDLL.dll");
            string story = Path.Combine(root, "main", "startup", "story.000");
            if (File.Exists(dll) && File.Exists(story))
                found.Add(new GameInstallationSettings(root, dll, story));
        }
        return found.OrderBy(x => x.GameRoot, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddSteamLibraries(string? steamPath, Action<string?> addRoot)
    {
        if (string.IsNullOrWhiteSpace(steamPath)) return;
        try
        {
            string steamRoot = Path.GetFullPath(steamPath);
            addRoot(Path.Combine(steamRoot, "steamapps", "common", "Divine Divinity"));
            string librariesFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(librariesFile)) return;

            foreach (string line in File.ReadAllLines(librariesFile))
            {
                int pathKey = line.IndexOf("\"path\"", StringComparison.Ordinal);
                if (pathKey < 0) continue;
                int valueStart = line.IndexOf('"', pathKey + 6);
                if (valueStart < 0) continue;
                int valueEnd = line.IndexOf('"', valueStart + 1);
                if (valueEnd <= valueStart) continue;
                string library = line.Substring(valueStart + 1, valueEnd - valueStart - 1);
                library = library.Replace(new string((char)92, 2), new string((char)92, 1));
                addRoot(Path.Combine(library, "steamapps", "common", "Divine Divinity"));
            }
        }
        catch { }
    }

    private static GameInstallationSettings? ChooseDiscoveredInstallation(
        Window owner, IReadOnlyList<GameInstallationSettings> candidates)
    {
        var dialog = new Window
        {
            Owner = owner, Title = "Choose Divine Divinity installation",
            Width = 720, Height = 390, MinWidth = 520, MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 38)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224))
        };
        GameInstallationSettings? selected = null;
        var root = new DockPanel { Margin = new Thickness(12) };
        var note = new TextBlock
        {
            Text = "Several compatible installations were found. Choose the copy to use for builds:",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(4) });
        var use = new Button { Content = "Use selected", IsDefault = true, MinWidth = 110, Margin = new Thickness(4) };
        buttons.Children.Add(use);
        root.Children.Add(buttons);
        var list = new ListBox { ItemsSource = candidates, DisplayMemberPath = nameof(GameRoot), SelectedIndex = 0 };
        root.Children.Add(list);
        use.Click += (_, _) =>
        {
            selected = list.SelectedItem as GameInstallationSettings;
            if (selected is not null) dialog.DialogResult = true;
        };
        dialog.Content = root;
        return dialog.ShowDialog() == true ? selected : null;
    }

    public static GameInstallationSettings? PromptForInstallation(Window owner)
    {
        var candidates = DiscoverInstallations();
        GameInstallationSettings? discovered = null;
        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            if (MessageBox.Show(owner,
                "A compatible Divine Divinity installation was found:\n\n" + candidate.GameRoot +
                "\n\nUse this installation?",
                "Game installation found", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                discovered = candidate;
        }
        else if (candidates.Count > 1)
        {
            discovered = ChooseDiscoveredInstallation(owner, candidates);
        }

        if (discovered is not null)
        {
            discovered.Save();
            return discovered;
        }

        var dllDialog = new OpenFileDialog
        {
            Title = "Select OsirisDLL.dll in your Divine Divinity installation",
            Filter = "OsirisDLL.dll|OsirisDLL.dll|DLL files (*.dll)|*.dll",
            FileName = "OsirisDLL.dll", CheckFileExists = true
        };
        if (dllDialog.ShowDialog(owner) != true) return null;

        string dllPath = Path.GetFullPath(dllDialog.FileName);
        string gameRoot = Path.GetDirectoryName(dllPath)!;
        string baseStoryPath = Path.Combine(gameRoot, "main", "startup", "story.000");
        if (!File.Exists(baseStoryPath))
        {
            string startup = Path.Combine(gameRoot, "main", "startup");
            var storyDialog = new OpenFileDialog
            {
                Title = "Select the base main\\startup\\story.000 file",
                Filter = "Divine Divinity story archive (story.000)|story.000|All files (*.*)|*.*",
                FileName = "story.000", CheckFileExists = true,
                InitialDirectory = Directory.Exists(startup) ? startup : gameRoot
            };
            if (storyDialog.ShowDialog(owner) != true) return null;
            baseStoryPath = Path.GetFullPath(storyDialog.FileName);
        }

        var settings = new GameInstallationSettings(gameRoot, dllPath, baseStoryPath);
        if (!settings.Validate(out string error))
        {
            MessageBox.Show(owner, error, "Invalid game installation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        settings.Save();
        return settings;
    }
}
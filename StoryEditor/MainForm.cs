using Microsoft.VisualBasic.Logging;
using System;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using static StoryEditor.MainForm;
using static System.ComponentModel.Design.ObjectSelectorEditor;
using static System.Windows.Forms.DataFormats;
using static System.Windows.Forms.LinkLabel;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace StoryEditor
{
    public partial class MainForm : Form
    {
        public string pathToStoryFile = "";
        public string pathToStoryBinFile = "story.000";
        public string pathToDivFile = "";
        public string pathToGameFolder = "";
        public string pathToMainProgramFolder = "";
        string pathToOsirisDllFile = "";
        public string[]? Story;
        public static List<Objects> objects = new List<Objects>();
        public static List<Goal> goals = new List<Goal>();
        public static List<string> subGoals = new List<string>();
        public static List<int[]> subGoalsInt = new List<int[]>();
        public static List<string> rules_call = new List<string>();
        public static List<string> rules_event = new List<string>();
        public static List<string> rules_query = new List<string>();
        public static List<string> rules_syscall = new List<string>();
        public static List<string> rules_sysquery = new List<string>();
        private string stringFilter = "";
        public int selectedGoal = -1;
        public bool compile_trace = false;
        public bool debug_trace = false;
        public bool build_and_run_game = false;
        string storyVersion = "";

        public MainForm()
        {
            InitializeComponent();
            pathToMainProgramFolder = AppDomain.CurrentDomain.BaseDirectory;
            // Читаем rules
            if (File.Exists("rules.000"))
            {
                string[] lines = File.ReadLines("rules.000").ToArray();
                foreach (var l in lines)
                {
                    string[] words = l.Split(' ');
                    if (words[0] == "call")
                    {
                        int x = l.LastIndexOf("(", StringComparison.CurrentCulture);
                        string rules = l.Remove(x).Remove(0, words[0].Length + 1);
                        rules_call.Add(rules);
                    }
                    if (words[0] == "event")
                    {
                        int x = l.LastIndexOf("(", StringComparison.CurrentCulture);
                        string rules = l.Remove(x).Remove(0, words[0].Length + 1);
                        rules_event.Add(rules);
                    }
                    if (words[0] == "query")
                    {
                        int x = l.LastIndexOf("(", StringComparison.CurrentCulture);
                        string rules = l.Remove(x).Remove(0, words[0].Length + 1);
                        rules_query.Add(rules);
                    }
                    if (words[0] == "syscall")
                    {
                        int x = l.LastIndexOf("(", StringComparison.CurrentCulture);
                        string rules = l.Remove(x).Remove(0, words[0].Length + 1);
                        rules_syscall.Add(rules);
                    }
                    if (words[0] == "sysquery")
                    {
                        int x = l.LastIndexOf("(", StringComparison.CurrentCulture);
                        string rules = l.Remove(x).Remove(0, words[0].Length + 1);
                        rules_sysquery.Add(rules);
                    }
                }
            }
            // Читаем конфиг
            if (File.Exists("config.ini"))
            {
                string[] lines = File.ReadLines("config.ini").ToArray();
                if (lines.Length > 2 && File.Exists(lines[0]))
                {
                    pathToDivFile = lines[0];
                    pathToGameFolder = lines[1];
                    pathToStoryBinFile = lines[2];
                    pathToOsirisDllFile = pathToGameFolder + "\\OsirisDLL.dll";
                    if (!File.Exists(pathToOsirisDllFile))
                    {
                        File.Delete("config.ini");
                        Application.Exit();
                    }
                }
                else
                {
                    OpenFileDialog Div = new OpenFileDialog();
                    Div.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    Div.Filter = "Div.exe (*.exe)|*.exe|All files (*.*)|*.*";
                    if (Div.ShowDialog() == DialogResult.OK)
                    {
                        pathToDivFile = Div.FileName;
                        int x = pathToDivFile.LastIndexOf("\\", StringComparison.CurrentCulture);
                        pathToGameFolder = pathToDivFile.Remove(x);
                        pathToStoryBinFile = pathToDivFile.Remove(x) + "\\main\\startup\\story.000";
                        pathToOsirisDllFile = pathToGameFolder + "\\OsirisDLL.dll";
                        if (File.Exists(pathToOsirisDllFile))
                        {
                            lines = new string[3];
                            lines[0] = pathToDivFile;
                            lines[1] = pathToGameFolder;
                            lines[2] = pathToStoryBinFile;
                            File.WriteAllLines("config.ini", lines);
                        }
                        else
                        {
                            File.Delete("config.ini");
                            Application.Exit();
                        }
                    }
                }
            }
            else
            {
                if (pathToDivFile == "")
                {
                    OpenFileDialog Div = new OpenFileDialog();
                    Div.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    Div.Filter = "Div.exe (*.exe)|*.exe|All files (*.*)|*.*";
                    if (Div.ShowDialog() == DialogResult.OK)
                    {
                        pathToDivFile = Div.FileName;
                        int x = pathToDivFile.LastIndexOf("\\", StringComparison.CurrentCulture);
                        pathToGameFolder = pathToDivFile.Remove(x);
                        pathToStoryBinFile = pathToDivFile.Remove(x) + "\\main\\startup\\story.000";
                        pathToOsirisDllFile = pathToGameFolder + "\\OsirisDLL.dll";
                        if (File.Exists(pathToOsirisDllFile))
                        {
                            String[] lines = [pathToDivFile, pathToGameFolder, pathToStoryBinFile];
                            File.WriteAllLines("config.ini", lines);
                            pathToDivFile = Div.FileName;
                        }
                        else
                        {
                            Application.Exit();
                        }
                    }
                    else
                    {
                        Application.Exit();
                    }
                }
            }
            ZipFile.ExtractToDirectory(Environment.CurrentDirectory + "\\plugin.zip", pathToGameFolder, true);
            File.Copy(pathToOsirisDllFile, Environment.CurrentDirectory + "\\OsirisDLL.dll", true);

            ConsoleRichTextBox.AppendText("Game directory: " + pathToGameFolder + "\n");
            ConsoleRichTextBox.AppendText("Path to div.exe: " + pathToDivFile + "\n");
            ConsoleRichTextBox.AppendText("Path to story.000: " + pathToStoryBinFile + "\n");
            //---------------------------------------------------------------------------------------
            // Добавляем элементы в контекстное меню для области KB
            KBRichTextBox.ContextMenuStrip = KBContextMenuStrip;
            ToolStripMenuItem Call = new ToolStripMenuItem("Call");
            ToolStripMenuItem Event = new ToolStripMenuItem("Event");
            ToolStripMenuItem Query = new ToolStripMenuItem("Query");
            ToolStripMenuItem Syscall = new ToolStripMenuItem("Syscall");
            ToolStripMenuItem Sysquery = new ToolStripMenuItem("Sysquery");
            KBContextMenuStrip.Items.AddRange(new[] { Call, Event, Query, Syscall });
            foreach (var item in rules_call)
            {
                Call.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in rules_event)
            {
                Event.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in rules_query)
            {
                Query.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in rules_syscall)
            {
                Syscall.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in rules_sysquery)
            {
                Sysquery.DropDownItems.Add(item, null, RulesClickHandler);
            }
        }
        //---------------------------------------------------------------------------------------
        private void StoryUnpack(string[] story)
        {
            objects.Clear();
            goals.Clear();
            subGoals.Clear();
            subGoalsInt.Clear();

            bool INIT = false;
            bool KB = false;
            bool EXIT = false;
            ConsoleRichTextBox.Text = "";
            foreach (string storyLine in story)
            {
                string[] words = storyLine.Split(' ');
                if (words[0] == "object" && words[1] == "{")
                {
                    objects.Add(new Objects(
                        words[2].Remove(words[2].Length - 1),
                        words[3].Remove(words[3].Length - 1),
                        words[6].Remove(words[6].Length - 1)));
                }
                if (storyLine.Length > 15 && storyLine.Remove(5) == "Goal(")
                {
                    INIT = false;
                    KB = false;
                    EXIT = false;
                    words = storyLine.Split('(');
                    if (words[1].Remove(0, words[1].Length - 5) == "Title")
                    {
                        string name = words[2].Remove(0, 1);
                        name = name.Remove(name.Length - 3);
                        if (Int32.TryParse(words[1].Remove(5), out int x))
                        {
                            goals.Add(new Goal(x, name));
                        }
                        else
                        {
                            if (Int32.TryParse(words[1].Remove(4), out x))
                            {
                                goals.Add(new Goal(x, name));
                            }
                            else
                            {
                                if (Int32.TryParse(words[1].Remove(3), out x))
                                {
                                    goals.Add(new Goal(x, name));
                                }
                                else
                                {
                                    if (Int32.TryParse(words[1].Remove(2), out x))
                                    {
                                        goals.Add(new Goal(x, name));
                                    }
                                    else
                                    {
                                        if (Int32.TryParse(words[1].Remove(1), out x))
                                        {
                                            goals.Add(new Goal(x, name));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (storyLine == "}")
                {
                    INIT = false;
                    KB = false;
                    EXIT = false;
                }
                if (storyLine == "INIT {")
                {
                    INIT = true;
                    KB = false;
                    EXIT = false;
                }
                if (storyLine == "KB {")
                {
                    INIT = false;
                    KB = true;
                    EXIT = false;
                }
                if (storyLine == "EXIT {")
                {
                    INIT = false;
                    KB = false;
                    EXIT = true;
                }
                if (INIT && storyLine != "INIT {")
                {
                    goals[^1].INIT.Add(storyLine);
                }
                if (KB && storyLine != "KB {")
                {
                    goals[^1].KB.Add(storyLine);
                }
                if (EXIT && storyLine != "EXIT {")
                {
                    goals[^1].EXIT.Add(storyLine);
                }
                // Story version
                if (storyLine.Contains("version \"", StringComparison.CurrentCulture))
                {
                    storyVersion = storyLine;
                    ConsoleRichTextBox.AppendText("Story " + storyLine + "\n");
                }
                // SubGoals
                words = storyLine.Split('.');
                if (words.Length > 1 && words[1].Contains("SubGoal", StringComparison.CurrentCulture))
                {
                    subGoals.Add(storyLine);
                }
            }
            foreach (var SG in subGoals)
            {
                string goal_string = SG.Remove(0, 5);
                int x = goal_string.IndexOf(")", StringComparison.CurrentCulture);
                goal_string = goal_string.Remove(x, goal_string.Length - x);
                string subgoal_string = SG;
                x = subgoal_string.LastIndexOf("(", StringComparison.CurrentCulture);
                subgoal_string = subgoal_string.Remove(0, x + 1);
                subgoal_string = subgoal_string.Remove(subgoal_string.Length - 2, 2);
                int goal = 0;
                if (Int32.TryParse(goal_string, out int c))
                {
                    goal = c;
                }
                int subgoal = 0;
                if (Int32.TryParse(subgoal_string, out c))
                {
                    subgoal = c;
                }
                subGoalsInt.Add([goal, subgoal]);
            }
            UpdateObjects();
            ConsoleRichTextBox.AppendText(pathToStoryFile + " unpack" + "\n");
            selectedGoal = 0;
            ReadGoal(0);
            addRuleButton.Enabled = true;
        }
        //---------------------------------------------------------------------------------------
        private void SaveStory(
            string patch,
            bool C_trace,
            bool D_trace,
            List<Objects> O,
            List<Goal> G,
            string ver)
        {
            if (selectedGoal >= 0)
            {
                goals[selectedGoal].INIT.Clear();
                foreach (var line in INITRichTextBox.Lines)
                {
                    goals[selectedGoal].INIT.Add(line);
                }
                goals[selectedGoal].KB.Clear();
                foreach (var line in KBRichTextBox.Lines)
                {
                    goals[selectedGoal].KB.Add(line);
                }
                goals[selectedGoal].EXIT.Clear();
                foreach (var line in EXITRichTextBox.Lines)
                {
                    goals[selectedGoal].EXIT.Add(line);
                }
            }

            TextWriter tw = new StreamWriter(patch, false);
            if (C_trace)
            {
                tw.WriteLine("option compile_trace");
            }
            else
            {
                tw.WriteLine("// option compile_trace");
            }
            if (D_trace)
            {
                tw.WriteLine("option debug_trace");
            }
            else
            {
                tw.WriteLine("// option debug_trace");
            }
            tw.WriteLine("");
            tw.WriteLine("type { NPC, 4 }");
            tw.WriteLine("type { OBJECT, 5 }");
            tw.WriteLine("type { DIALOG, 6 }");
            tw.WriteLine("type { REGION, 7 }");
            tw.WriteLine("type { LOCATION, 8 }");
            tw.WriteLine("type { NPC_CLASS, 9 }");
            tw.WriteLine("type { OBJECT_CLASS, 10 }");
            tw.WriteLine("type { DIALOG_EVENT, 11 }");
            tw.WriteLine("type { ENGINE, 12 }");
            tw.WriteLine("type { FUNCTION, 13 }");
            tw.WriteLine("type { SREGION, 15 }");
            tw.WriteLine("");

            foreach (var o in O)
            {
                tw.WriteLine("object { " + o.name + ", " + o.type + ", ( " + o.type + ", " + o.ID + ", 0, 0 ) }");
            }
            tw.WriteLine("");


            if (File.Exists("rules.000"))
            {
                string[] inst = System.IO.File.ReadLines("rules.000").ToArray();
                foreach (var i in inst)
                {
                    tw.WriteLine(i);
                }
            }
            else ConsoleRichTextBox.AppendText("rules.000 not found" + "\n");

            tw.WriteLine("");
            tw.WriteLine(ver);
            tw.WriteLine("");

            foreach (var g in G)
            {
                tw.WriteLine("Goal(" + g.ID + ").Title(\"" + g.NAME + "\");");
                tw.WriteLine("Goal(" + g.ID + ") {");
                tw.WriteLine("INIT {");
                foreach (var i in g.INIT)
                {
                    tw.WriteLine(i);
                }
                tw.WriteLine("}");
                tw.WriteLine("");
                tw.WriteLine("KB {");
                foreach (var k in g.KB)
                {
                    tw.WriteLine(k);
                }
                tw.WriteLine("}");
                tw.WriteLine("");
                tw.WriteLine("EXIT {");
                foreach (var e in g.EXIT)
                {
                    tw.WriteLine(e);
                }
                tw.WriteLine("}");
                tw.WriteLine("");
                tw.WriteLine("}");
                tw.WriteLine("");
            }

            foreach (var g in goals)
            {
                if (g.parent.Count == 0)
                {
                    tw.WriteLine("Goal(" + g.ID + ").SubGoals(OR);");
                }
                else
                {
                    tw.WriteLine("Goal(" + g.ID + ").SubGoals(AND);");
                }
                if (g.child.Count > 0)
                {
                    foreach (var ch in g.child)
                    {
                        tw.WriteLine("Goal(" + g.ID + ").SubGoal(" + (ch + 1) + ");");
                    }
                }
            }

            tw.Close();
            ConsoleRichTextBox.AppendText(patch + " save" + "\n");
        }
        //---------------------------------------------------------------------------------------
        private void UpdateObjects()
        {
            NPCListBox.Items.Clear();
            OBJECTListBox.Items.Clear();
            DIALOGListBox.Items.Clear();
            REGIONListBox.Items.Clear();
            LOCATIONListBox.Items.Clear();
            NPC_CLASSListBox.Items.Clear();
            OBJECT_CLASSListBox.Items.Clear();
            DIALOG_EVENTListBox.Items.Clear();
            ENGINEListBox.Items.Clear();
            FUNCTIONListBox.Items.Clear();
            SREGIONListBox.Items.Clear();

            objects.Sort((x, y) => x.ID.CompareTo(y.ID));
            foreach (var obj in objects)
            {
                if (obj.type == 4) NPCListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 5) OBJECTListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 6) DIALOGListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 7) REGIONListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 8) LOCATIONListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 9) NPC_CLASSListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 10) OBJECT_CLASSListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 11) DIALOG_EVENTListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 12) ENGINEListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 13) FUNCTIONListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);
                if (obj.type == 15) SREGIONListBox.Items.Add(obj.ID.ToString().PadRight(6, ' ') + obj.name);

            }
        }
        //---------------------------------------------------------------------------------------
        static void HighlightPhrase(RichTextBox box, string phrase, Color color)
        {
            int pos = box.SelectionStart;
            string s = box.Text;
            for (int ix = 0; ;)
            {
                int jx = s.IndexOf(phrase, ix, StringComparison.CurrentCulture);
                if (jx < 0) break;
                int a = s.IndexOf("\n", jx);
                if (jx + phrase.Length == a)
                {
                    box.SelectionStart = jx;
                    box.SelectionLength = phrase.Length;
                    box.SelectionColor = color;
                }
                ix = jx + 1;
            }
            box.SelectionStart = pos;
            box.SelectionLength = 0;
        }
        //---------------------------------------------------------------------------------------
        private void ColoredWords()
        {
            HighlightPhrase(KBRichTextBox, "IF", Color.Orange);
            HighlightPhrase(KBRichTextBox, "AND", Color.Orange);
            HighlightPhrase(KBRichTextBox, "NOT", Color.Orange);
            HighlightPhrase(KBRichTextBox, "AND NOT", Color.Orange);
            HighlightPhrase(KBRichTextBox, "THEN", Color.Orange);
            HighlightPhrase(KBRichTextBox, "PROC", Color.Red);
            HighlightPhrase(KBRichTextBox, "GoalCompleted;", Color.BlueViolet);
        }
        //---------------------------------------------------------------------------------------
        private void INITRichTextBox_Leave(object sender, EventArgs e)
        {
            if (selectedGoal >= 0)
            {
                ColoredWords();
                goals[selectedGoal].INIT.Clear();
                foreach (var line in INITRichTextBox.Lines)
                {
                    goals[selectedGoal].INIT.Add(line);
                }
            }
        }
        //---------------------------------------------------------------------------------------
        private void KBRichTextBox_Leave(object sender, EventArgs e)
        {
            if (selectedGoal >= 0)
            {
                ColoredWords();
                goals[selectedGoal].KB.Clear();
                foreach (var line in KBRichTextBox.Lines)
                {
                    goals[selectedGoal].KB.Add(line);
                }
            }
        }
        //---------------------------------------------------------------------------------------
        private void EXITRichTextBox_Leave(object sender, EventArgs e)
        {
            if (selectedGoal >= 0)
            {
                ColoredWords();
                goals[selectedGoal].EXIT.Clear();
                foreach (var line in EXITRichTextBox.Lines)
                {
                    goals[selectedGoal].EXIT.Add(line);
                }
            }
        }
        //---------------------------------------------------------------------------------------
        private void compiletraceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            compile_trace = !compile_trace;
            compiletraceToolStripMenuItem.Checked = compile_trace;
        }
        //---------------------------------------------------------------------------------------
        private void debugtraceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            debug_trace = !debug_trace;
            debugtraceToolStripMenuItem.Checked = debug_trace;
        }
        //---------------------------------------------------------------------------------------
        private void SaveButton_Click(object sender, EventArgs e)
        {
            SaveStory(pathToStoryFile, compile_trace, debug_trace, objects, goals, storyVersion);
        }
        //---------------------------------------------------------------------------------------
        private void BuildButton_Click(object sender, EventArgs e) // Сохраняем и компилируем бинарник
        {
            if (selectedGoal >= 0)
            {
                ConsoleRichTextBox.AppendText("");
                bool ready = true;
                if (pathToStoryFile == "")
                {
                    SaveFileDialog SF = new SaveFileDialog();
                    SF.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    SF.Filter = "Divinity Scripts(*.div)|*.div|All files(*.*)|*.*";
                    if (SF.ShowDialog() == DialogResult.OK)
                    {
                        pathToStoryFile = SF.FileName;
                        SaveStory(pathToStoryFile, compile_trace, debug_trace, objects, goals, storyVersion);
                    }
                    else
                    {
                        ready = false;
                    }
                }
                else
                {
                    SaveStory(pathToStoryFile, compile_trace, debug_trace, objects, goals, storyVersion);
                }
                if (ready)
                {
                    if (build_and_run_game) // Запускаем игру
                    {
                        Environment.CurrentDirectory = pathToMainProgramFolder;
                        ProcessStartInfo BuildStory = new()
                        {
                            WorkingDirectory = pathToMainProgramFolder,
                            FileName = @"OsirisCC.exe",
                            Arguments = " -log compile.log -compile \"" + pathToStoryFile + "\" -save \"" + pathToStoryBinFile + "\"",
                            UseShellExecute = true
                        };
                        Process? build = Process.Start(BuildStory);
                        build?.WaitForExit();
                        if(!LogRead())
                        {
                            ConsoleRichTextBox.AppendText("Compilation complete" + "\n");
                            Environment.CurrentDirectory = pathToGameFolder;
                            ProcessStartInfo GameStart = new()
                            {
                                WorkingDirectory = pathToGameFolder,
                                FileName = pathToDivFile,
                                UseShellExecute = true
                            };
                            Process? game = Process.Start(GameStart);
                            game?.WaitForExit();
                        }
                        Environment.CurrentDirectory = pathToMainProgramFolder;
                    }
                    else
                    {
                        Environment.CurrentDirectory = pathToMainProgramFolder;
                        ProcessStartInfo BuildStory = new()
                        {
                            WorkingDirectory = pathToMainProgramFolder,
                            FileName = @"OsirisCC.exe",
                            Arguments = " -log compile.log -compile \"" + pathToStoryFile + "\" -save \"" + pathToStoryBinFile + "\"",
                            UseShellExecute = true
                        };
                        Process? build = Process.Start(BuildStory);
                        build?.WaitForExit();
                        if (!LogRead())
                        {
                            ConsoleRichTextBox.AppendText("Compilation complete" + "\n");
                        }
                    }
                }
            }
        }
        //---------------------------------------------------------------------------------------
        private bool LogRead()
        {
            // line 14703: error: syntax error: "IF"  unexpected.
            // line 14710: error: rule action part syntax: missing ';'
            bool errorPresent = false;
            if (File.Exists("compile.log"))
            {
                System.Collections.Generic.IEnumerable<String> lines = File.ReadLines("compile.log");
                ConsoleRichTextBox.Text = "";
                foreach (string line in lines)
                {
                    if (line.Length > 0)
                    {
                        string[] words = line.Split(' ');
                        if (words[0].Equals("line"))
                        {
                            errorPresent = true;
                            ConsoleRichTextBox.Text += line + "\n";
                        }
                    }
                }
            }
            return errorPresent;
        }
        //---------------------------------------------------------------------------------------
        private void buildAndRunGameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            build_and_run_game = !build_and_run_game;
            buildAndRunGameToolStripMenuItem.Checked = build_and_run_game;
        }
        //---------------------------------------------------------------------------------------
        private void originalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (File.Exists("StoryOriginal.000"))
            {
                Story = System.IO.File.ReadLines("StoryOriginal.000").ToArray();
                StoryUnpack(Story);
                GoalTree();
            }
            else
            {
                ConsoleRichTextBox.AppendText("File StoryOriginal.000 not found" + "\n");
            }
            pathToStoryFile = "";
        }
        //---------------------------------------------------------------------------------------
        private void customToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog OPF = new OpenFileDialog();
            OPF.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            OPF.Filter = "story.div (*.div)|*.div|All files (*.*)|*.*";
            if (OPF.ShowDialog() == DialogResult.OK)
            {
                pathToStoryFile = OPF.FileName;
                ConsoleRichTextBox.AppendText("Open file: " + pathToStoryFile + "\n");
                Story = System.IO.File.ReadLines(pathToStoryFile).ToArray();
                StoryUnpack(Story);
                GoalTree();
            }
        }
        //---------------------------------------------------------------------------------------
        private void saveStoryAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog SF = new SaveFileDialog();
            SF.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            SF.Filter = "Divinity Scripts(*.div)|*.div|All files(*.*)|*.*";
            if (SF.ShowDialog() == DialogResult.OK)
            {
                pathToStoryFile = SF.FileName;
                SaveStory(pathToStoryFile, compile_trace, debug_trace, objects, goals, storyVersion);
            }
        }
        //---------------------------------------------------------------------------------------
        private void saveStoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (pathToStoryFile == "")
            {
                SaveFileDialog SF = new SaveFileDialog();
                SF.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                SF.Filter = "Divinity Scripts(*.div)|*.div|All files(*.*)|*.*";
                if (SF.ShowDialog() == DialogResult.OK)
                {
                    pathToStoryFile = SF.FileName;
                    SaveStory(pathToStoryFile, compile_trace, debug_trace, objects, goals, storyVersion);
                }
            }
            else
            {
                SaveStory(pathToStoryFile, compile_trace, debug_trace, objects, goals, storyVersion);
            }
        }
        //---------------------------------------------------------------------------------------
        private void RulesClickHandler(object? sender, EventArgs e)
        {
            KBRichTextBox.SelectionLength = 0;
            KBRichTextBox.SelectedText = sender?.ToString() + "\n";
        }
        //---------------------------------------------------------------------------------------
        public void GoalTree()
        {
            GoalTreeView.Nodes.Clear();
            foreach (var SG in subGoalsInt)
            {
                int goal = SG[0];
                int subgoal = SG[1];
                if (subgoal > 0)
                {
                    if (!goals[subgoal - 1].parent.Contains(goal - 1))
                    {
                        goals[subgoal - 1].parent.Add(goal - 1);
                    }
                    if (!goals[goal - 1].child.Contains(subgoal - 1))
                    {
                        goals[goal - 1].child.Add(subgoal - 1);
                    }
                }
            }
            foreach (var g in goals)
            {
                TreeNode t;
                if (g.parent.Count == 0 && g.child.Count == 0)
                {
                    t = new TreeNode(g.ID + " " + g.NAME)
                    {
                        Tag = g.ID
                    };
                    GoalTreeView.Nodes.Add(t);
                }
                else
                {
                    if (g.parent.Count == 0)
                    {
                        t = new TreeNode(g.ID + " " + g.NAME)
                        {
                            Tag = g.ID
                        };
                        GoalTreeView.Nodes.Add(AddNode(t, g.child));
                    }
                }
            }
            GoalTreeView.HideSelection = false;
        }
        private void GoalTreeRefresh()
        {
            GoalTreeView.Nodes.Clear();
            foreach (var g in goals)
            {
                TreeNode t;
                if (g.parent.Count == 0 && g.child.Count == 0)
                {
                    t = new TreeNode(g.ID + " " + g.NAME)
                    {
                        Tag = g.ID
                    };
                    GoalTreeView.Nodes.Add(t);
                }
                else
                {
                    if (g.parent.Count == 0)
                    {
                        t = new TreeNode(g.ID + " " + g.NAME)
                        {
                            Tag = g.ID
                        };
                        GoalTreeView.Nodes.Add(AddNode(t, g.child));
                    }
                }
            }
            GoalTreeView.HideSelection = false;
        }
        //---------------------------------------------------------------------------------------
        public TreeNode AddNode(TreeNode tn, List<int> child)
        {
            foreach (var ch in child)
            {
                if (goals[ch].child.Count != 0)
                {
                    TreeNode t = new TreeNode(goals[ch].ID + " " + goals[ch].NAME)
                    {
                        Tag = goals[ch].ID
                    };
                    tn.Nodes.Add(AddNode(t, goals[ch].child));
                }
                else
                {
                    tn.Nodes.Add(new TreeNode(goals[ch].ID + " " + goals[ch].NAME)
                    {
                        Tag = goals[ch].ID
                    });
                }
            }
            return tn;
        }
        //---------------------------------------------------------------------------------------
        private void GoalTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            selectedGoal = Int32.Parse(GoalTreeView.SelectedNode.Tag.ToString() + "") - 1;
            ReadGoal(selectedGoal);
        }
        //---------------------------------------------------------------------------------------
        private void ReadGoal(int selectGoal)
        {
            INITRichTextBox.Text = "";
            KBRichTextBox.Text = "";
            EXITRichTextBox.Text = "";
            string buffer = "";
            foreach (var s in goals[selectGoal].INIT)
            {
                buffer += s + System.Environment.NewLine;
            }
            INITRichTextBox.Text += buffer;
            buffer = "";
            foreach (var s in goals[selectGoal].KB)
            {
                buffer += s + System.Environment.NewLine;
            }
            KBRichTextBox.Text += buffer;
            buffer = "";
            foreach (var s in goals[selectGoal].EXIT)
            {
                buffer += s + System.Environment.NewLine;
            }
            EXITRichTextBox.Text += buffer;
            ColoredWords();
        }
        //---------------------------------------------------------------------------------------
        private void FilterComboBox_TextUpdate(object sender, EventArgs e)
        {
            stringFilter = FilterComboBox.Text;
            FilterComboBox.Items.Clear();
            FilterComboBox.Items.Add(FilterComboBox.Text);
            foreach (var g in goals)
            {
                if (g.NAME.Contains(FilterComboBox.Text, StringComparison.CurrentCultureIgnoreCase))
                {
                    FilterComboBox.Items.Add(g.ID + " " + g.NAME);
                }
            }
            Cursor.Current = Cursors.Default;
            FilterComboBox.DroppedDown = true;
            FilterComboBox.SelectedItem = null;
            FilterComboBox.SelectedIndex = -1;
            FilterComboBox.Text = stringFilter;
            FilterComboBox.SelectionStart = FilterComboBox.Text.Length;
        }
        //---------------------------------------------------------------------------------------
        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (FilterComboBox.SelectedIndex >= 0 && FilterComboBox.SelectedItem is not null)
            {
                string[] words = (FilterComboBox.SelectedItem.ToString() + "").Split(' ');
                if (Int32.TryParse(words[0], out int x))
                {
                    System.Diagnostics.Debug.WriteLine(x);
                    TreeNode? itemNode = null;
                    foreach (TreeNode node in GoalTreeView.Nodes)
                    {
                        itemNode = SearchNodeFromID(x, node);
                        if (itemNode != null) break;
                    }
                    System.Diagnostics.Debug.WriteLine(itemNode);
                    if (itemNode != null)
                    {
                        GoalTreeView.SelectedNode = itemNode;
                    }
                    else
                    {
                        var result = GoalTreeView.Nodes.OfType<TreeNode>()
                            .FirstOrDefault(node => node.Tag.Equals(x));
                        if (result != null)
                        {
                            GoalTreeView.SelectedNode = result;
                        }
                    }
                }
            }
        }
        //---------------------------------------------------------------------------------------
        public TreeNode? SearchNodeFromID(int itemId, TreeNode rootNode)
        {
            foreach (TreeNode node in rootNode.Nodes)
            {
                if (node.Tag.Equals(itemId)) return node;
                TreeNode? next = SearchNodeFromID(itemId, node);
                if (next != null) return next;
            }
            return null;
        }
        //---------------------------------------------------------------------------------------
        private void addRuleButton_Click(object sender, EventArgs e)
        {
            AddRule AR = new AddRule();
            AR.ShowDialog();
            GoalTree();
        }
    }
    public class Goal
    {
        public List<int> parent = new List<int>();
        public List<int> child = new List<int>();
        public int ID;
        public string NAME = "";
        public List<string> INIT = new List<string>();
        public List<string> KB = new List<string>();
        public List<string> EXIT = new List<string>();
        public Goal(int id, string name)
        {
            ID = id;
            NAME = name;
        }
        public Goal(int id, string name, List<string> init, List<string> kb, List<string> exit)
        {
            ID = id;
            NAME = name;
            INIT = init;
            KB = kb;
            EXIT = exit;
        }
        public void ClearParentAndChild()
        {
            parent.Clear();
            child.Clear();
        }
    }
    public class Objects
    {
        public string name = "";
        public int type;
        public int ID;
        public Objects(string name, string type, string ID)
        {
            this.name = name;
            this.type = Int32.Parse(type);
            this.ID = Int32.Parse(ID);
        }
    }
}

//System.Diagnostics.Debug.WriteLine();

//if (g.NAME.Contains(stringFilter, StringComparison.CurrentCultureIgnoreCase))
//{
//    GoalListBox.Items.Add(g.ID.ToString().PadRight(6, ' ') + g.NAME);
//}
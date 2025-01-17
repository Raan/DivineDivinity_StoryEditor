using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace StoryEditor
{
    public partial class AddRule : Form
    {
        public AddRule()
        {
            InitializeComponent();
            // Добавляем элементы в контекстное меню для области KB
            KBRichTextBox.ContextMenuStrip = KBContextMenuStrip;
            ToolStripMenuItem Call = new ToolStripMenuItem("Call");
            ToolStripMenuItem Event = new ToolStripMenuItem("Event");
            ToolStripMenuItem Query = new ToolStripMenuItem("Query");
            ToolStripMenuItem Syscall = new ToolStripMenuItem("Syscall");
            ToolStripMenuItem Sysquery = new ToolStripMenuItem("Sysquery");
            KBContextMenuStrip.Items.AddRange(new[] { Call, Event, Query, Syscall });
            foreach (var item in MainForm.rules_call)
            {
                Call.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in MainForm.rules_event)
            {
                Event.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in MainForm.rules_query)
            {
                Query.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in MainForm.rules_syscall)
            {
                Syscall.DropDownItems.Add(item, null, RulesClickHandler);
            }
            foreach (var item in MainForm.rules_sysquery)
            {
                Sysquery.DropDownItems.Add(item, null, RulesClickHandler);
            }
        }
        private void RulesClickHandler(object? sender, EventArgs e)
        {
            KBRichTextBox.SelectionLength = 0;
            KBRichTextBox.SelectedText = sender?.ToString() + "\n";
        }
        private void AddRuleButton_Click(object sender, EventArgs e)
        {
            if (NameRuleTextBox.Text.Length > 0)
            {
                int id = 0;
                string name = NameRuleTextBox.Text;
                List<string> init = new();
                List<string> kb = new();
                List<string> exit = new();
                foreach (string s in INITRichTextBox.Text.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    init.Add(s);
                }
                foreach (string s in KBRichTextBox.Text.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    kb.Add(s);
                }
                foreach (string s in EXITRichTextBox.Text.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    exit.Add(s);
                }
                int parent = 0;
                int child = 0;
                Int32.TryParse(ParentTextBox.Text, out parent );
                Int32.TryParse(ChildTextBox.Text, out child);

                //kb.Add("IF");
                //kb.Add("ObjectUsed(OBJECT_joram_chest, NPC_Hero)");
                //kb.Add("THEN");
                //kb.Add("TeleportToLocation(ENGINE_GAME, NPC_Hero, LOCATION_stps_Joram);");

                // Проверяем возможность добавить child
                bool childFlag = false;
                foreach(var g in MainForm.goals)
                {
                    if (g.ID == child && g.parent.Count == 0)
                    {
                        childFlag = true;
                        break;
                    }
                }
                if (!childFlag) errorLabel.Text = "error: impossible child!";
                // Проверяем совпадение имен правил
                bool nameFlag = true;
                foreach (var g in MainForm.goals)
                {
                    if (g.NAME.Equals(name))
                    {
                        nameFlag = false;
                        break;
                    }
                }
                if (!nameFlag) errorLabel.Text = "error: same names!";
                if (nameFlag && (childFlag || child == 0))
                {
                    errorLabel.Text = "";
                    // Очищаем зависимости
                    foreach (var g in MainForm.goals)
                    {
                        g.ClearParentAndChild();
                    }
                    // Подбираем id для нового правила с учетем алфавитной сортировки
                    foreach (var g in MainForm.goals)
                    {
                        List<string> compare = new();
                        compare.Add(name);
                        compare.Add(g.NAME);
                        compare.Sort((x, y) => x.CompareTo(y));
                        if (compare[0].Equals(name))
                        {
                            id = g.ID;
                            break;
                        }
                    }
                    // Вставляем новое правило
                    MainForm.goals.Insert(id - 1, new Goal(id, name, init, kb, exit));
                    // Увеличиваем id нижеследующих правил на 1
                    for (int i = id; i < MainForm.goals.Count; i++)
                    {
                        MainForm.goals[i].ID++;
                    }
                    // Ищем точку вставки нового правила в subGoalsInt
                    int insertIndex = 0;
                    for (int i = 0; i < MainForm.subGoalsInt.Count; i++)
                    {
                        if (id <= MainForm.subGoalsInt[i][0])
                        {
                            insertIndex = i; break;
                        }
                    }
                    // Вставляем новое правило в subGoalsInt
                    if (child < id)
                    {
                        MainForm.subGoalsInt.Insert(insertIndex, [id, child]);
                    }
                    else
                    {
                        MainForm.subGoalsInt.Insert(insertIndex, [id, child + 1]);
                    }
                    // Увеличиваем id нижеследующих правил на 1
                    for (int i = insertIndex + 1; i < MainForm.subGoalsInt.Count; i++)
                    {
                        MainForm.subGoalsInt[i][0]++;
                        if (MainForm.subGoalsInt[i][1] >= id) MainForm.subGoalsInt[i][1]++;
                    }
                    // Если у нового объекта есть parent, то прописываем ему новой child
                    if (parent > 0)
                    {
                        foreach (var sb in MainForm.subGoalsInt)
                        {
                            if (parent < id)
                            {
                                if (sb[0] == parent)
                                {
                                    sb[1] = id;
                                    break;
                                }
                            }
                            else
                            {
                                if (sb[0] == parent + 1)
                                {
                                    sb[1] = id;
                                    break;
                                }
                            }
                        }
                    }
                    Task.Delay(500);
                    this.Close();
                }
            }
        }

        private void ParentTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && (e.KeyChar != '.'))
            {
                e.Handled = true;
            }
            if ((e.KeyChar == '.') && ((sender as System.Windows.Forms.TextBox).Text.IndexOf('.') > -1))
            {
                e.Handled = true;
            }
        }

        private void ChildTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && (e.KeyChar != '.'))
            {
                e.Handled = true;
            }
            if ((e.KeyChar == '.') && ((sender as System.Windows.Forms.TextBox).Text.IndexOf('.') > -1))
            {
                e.Handled = true;
            }
        }
    }
}

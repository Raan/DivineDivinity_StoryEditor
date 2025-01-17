namespace StoryEditor
{
    partial class AddRule
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            NameRuleTextBox = new TextBox();
            label1 = new Label();
            AddRuleButton = new Button();
            label2 = new Label();
            ParentTextBox = new TextBox();
            label3 = new Label();
            ChildTextBox = new TextBox();
            groupBox1 = new GroupBox();
            errorLabel = new Label();
            groupBox2 = new GroupBox();
            splitContainer1 = new SplitContainer();
            INITBox = new GroupBox();
            INITRichTextBox = new RichTextBox();
            splitContainer2 = new SplitContainer();
            KBBox = new GroupBox();
            KBRichTextBox = new RichTextBox();
            EXITBox = new GroupBox();
            EXITRichTextBox = new RichTextBox();
            KBContextMenuStrip = new ContextMenuStrip(components);
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            INITBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
            splitContainer2.Panel1.SuspendLayout();
            splitContainer2.Panel2.SuspendLayout();
            splitContainer2.SuspendLayout();
            KBBox.SuspendLayout();
            EXITBox.SuspendLayout();
            SuspendLayout();
            // 
            // NameRuleTextBox
            // 
            NameRuleTextBox.Location = new Point(54, 15);
            NameRuleTextBox.Name = "NameRuleTextBox";
            NameRuleTextBox.Size = new Size(476, 23);
            NameRuleTextBox.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 19);
            label1.Name = "label1";
            label1.Size = new Size(42, 15);
            label1.TabIndex = 1;
            label1.Text = "Name:";
            // 
            // AddRuleButton
            // 
            AddRuleButton.Location = new Point(455, 49);
            AddRuleButton.Name = "AddRuleButton";
            AddRuleButton.Size = new Size(75, 23);
            AddRuleButton.TabIndex = 2;
            AddRuleButton.Text = "Add rule";
            AddRuleButton.UseVisualStyleBackColor = true;
            AddRuleButton.Click += AddRuleButton_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 53);
            label2.Name = "label2";
            label2.Size = new Size(44, 15);
            label2.TabIndex = 3;
            label2.Text = "Parent:";
            // 
            // ParentTextBox
            // 
            ParentTextBox.Location = new Point(54, 49);
            ParentTextBox.Name = "ParentTextBox";
            ParentTextBox.Size = new Size(56, 23);
            ParentTextBox.TabIndex = 4;
            ParentTextBox.KeyPress += ParentTextBox_KeyPress;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(116, 53);
            label3.Name = "label3";
            label3.Size = new Size(35, 15);
            label3.TabIndex = 5;
            label3.Text = "Child";
            // 
            // ChildTextBox
            // 
            ChildTextBox.Location = new Point(157, 49);
            ChildTextBox.Name = "ChildTextBox";
            ChildTextBox.Size = new Size(56, 23);
            ChildTextBox.TabIndex = 6;
            ChildTextBox.KeyPress += ChildTextBox_KeyPress;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(errorLabel);
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(ChildTextBox);
            groupBox1.Controls.Add(NameRuleTextBox);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(AddRuleButton);
            groupBox1.Controls.Add(ParentTextBox);
            groupBox1.Controls.Add(label2);
            groupBox1.Dock = DockStyle.Top;
            groupBox1.Location = new Point(0, 0);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(542, 83);
            groupBox1.TabIndex = 7;
            groupBox1.TabStop = false;
            // 
            // errorLabel
            // 
            errorLabel.AutoSize = true;
            errorLabel.Location = new Point(219, 53);
            errorLabel.Name = "errorLabel";
            errorLabel.Size = new Size(0, 15);
            errorLabel.TabIndex = 7;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(splitContainer1);
            groupBox2.Dock = DockStyle.Fill;
            groupBox2.Location = new Point(0, 83);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(542, 486);
            groupBox2.TabIndex = 8;
            groupBox2.TabStop = false;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(3, 19);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(INITBox);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(splitContainer2);
            splitContainer1.Size = new Size(536, 464);
            splitContainer1.SplitterDistance = 168;
            splitContainer1.TabIndex = 3;
            // 
            // INITBox
            // 
            INITBox.Controls.Add(INITRichTextBox);
            INITBox.Dock = DockStyle.Fill;
            INITBox.Location = new Point(0, 0);
            INITBox.Name = "INITBox";
            INITBox.Size = new Size(536, 168);
            INITBox.TabIndex = 0;
            INITBox.TabStop = false;
            INITBox.Text = "INIT";
            // 
            // INITRichTextBox
            // 
            INITRichTextBox.Dock = DockStyle.Fill;
            INITRichTextBox.Location = new Point(3, 19);
            INITRichTextBox.Name = "INITRichTextBox";
            INITRichTextBox.Size = new Size(530, 146);
            INITRichTextBox.TabIndex = 0;
            INITRichTextBox.Text = "";
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = DockStyle.Fill;
            splitContainer2.Location = new Point(0, 0);
            splitContainer2.Name = "splitContainer2";
            splitContainer2.Orientation = Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.Controls.Add(KBBox);
            // 
            // splitContainer2.Panel2
            // 
            splitContainer2.Panel2.Controls.Add(EXITBox);
            splitContainer2.Size = new Size(536, 292);
            splitContainer2.SplitterDistance = 144;
            splitContainer2.TabIndex = 0;
            // 
            // KBBox
            // 
            KBBox.Controls.Add(KBRichTextBox);
            KBBox.Dock = DockStyle.Fill;
            KBBox.Location = new Point(0, 0);
            KBBox.Name = "KBBox";
            KBBox.Size = new Size(536, 144);
            KBBox.TabIndex = 1;
            KBBox.TabStop = false;
            KBBox.Text = "KB";
            // 
            // KBRichTextBox
            // 
            KBRichTextBox.Dock = DockStyle.Fill;
            KBRichTextBox.Location = new Point(3, 19);
            KBRichTextBox.Name = "KBRichTextBox";
            KBRichTextBox.Size = new Size(530, 122);
            KBRichTextBox.TabIndex = 0;
            KBRichTextBox.Text = "";
            // 
            // EXITBox
            // 
            EXITBox.Controls.Add(EXITRichTextBox);
            EXITBox.Dock = DockStyle.Fill;
            EXITBox.Location = new Point(0, 0);
            EXITBox.Name = "EXITBox";
            EXITBox.Size = new Size(536, 144);
            EXITBox.TabIndex = 2;
            EXITBox.TabStop = false;
            EXITBox.Text = "EXIT";
            // 
            // EXITRichTextBox
            // 
            EXITRichTextBox.Dock = DockStyle.Fill;
            EXITRichTextBox.Location = new Point(3, 19);
            EXITRichTextBox.Name = "EXITRichTextBox";
            EXITRichTextBox.Size = new Size(530, 122);
            EXITRichTextBox.TabIndex = 0;
            EXITRichTextBox.Text = "";
            // 
            // KBContextMenuStrip
            // 
            KBContextMenuStrip.ImageScalingSize = new Size(20, 20);
            KBContextMenuStrip.Name = "KBContextMenuStrip";
            KBContextMenuStrip.Size = new Size(181, 26);
            // 
            // AddRule
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(542, 569);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            MaximumSize = new Size(550, 1024);
            MinimumSize = new Size(550, 600);
            Name = "AddRule";
            StartPosition = FormStartPosition.CenterParent;
            Text = "AddRule";
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            INITBox.ResumeLayout(false);
            splitContainer2.Panel1.ResumeLayout(false);
            splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
            splitContainer2.ResumeLayout(false);
            KBBox.ResumeLayout(false);
            EXITBox.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TextBox NameRuleTextBox;
        private Label label1;
        private Button AddRuleButton;
        private Label label2;
        private TextBox ParentTextBox;
        private Label label3;
        private TextBox ChildTextBox;
        private GroupBox groupBox1;
        private GroupBox groupBox2;
        private GroupBox INITBox;
        private GroupBox EXITBox;
        private GroupBox KBBox;
        private SplitContainer splitContainer1;
        private SplitContainer splitContainer2;
        private RichTextBox INITRichTextBox;
        private RichTextBox KBRichTextBox;
        private RichTextBox EXITRichTextBox;
        private Label errorLabel;
        private ContextMenuStrip KBContextMenuStrip;
    }
}
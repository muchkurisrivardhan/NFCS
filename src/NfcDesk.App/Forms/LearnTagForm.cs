using System.Runtime.Versioning;
using NfcDesk.Core.Config;
using NfcDesk.Core.Scripts;

namespace NfcDesk.App.Forms;

/// <summary>Modal dialog to map a tag to a script + label. Used for learning a new tag and for editing.</summary>
[SupportedOSPlatform("windows")]
public sealed class LearnTagForm : Form
{
    private readonly ScriptLibrary _library;
    private readonly TagMapping _source;
    private readonly ComboBox _script = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _label = new() { Dock = DockStyle.Fill, MaxLength = 60 };

    public TagMapping? Result { get; private set; }

    public LearnTagForm(ScriptLibrary library, TagMapping source, bool editMode)
    {
        _library = library;
        _source = source;

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Text = editMode ? "Edit tag" : "New tag";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(440, 190);

        var scripts = library.ListScripts();
        foreach (var s in scripts) _script.Items.Add(s);
        if (!string.IsNullOrEmpty(source.Script))
        {
            int idx = _script.FindStringExact(source.Script);
            if (idx < 0) { _script.Items.Add(source.Script); idx = _script.Items.Count - 1; }
            _script.SelectedIndex = idx;
        }
        else if (_script.Items.Count > 0) _script.SelectedIndex = 0;

        _label.Text = string.IsNullOrEmpty(source.Label) ? source.Tag : source.Label;

        var prompt = new Label
        {
            Text = $"New tag '{source.Tag}' — pick a script and a label.",
            AutoSize = true,
            MaximumSize = new Size(410, 0),
            Margin = new Padding(0, 0, 0, 8),
        };
        if (editMode) prompt.Text = $"Tag '{source.Tag}' — change its script or label.";

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(prompt, 0, 0);
        grid.SetColumnSpan(prompt, 2);
        grid.Controls.Add(new Label { Text = "Script:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, 1);
        grid.Controls.Add(_script, 1, 1);
        grid.Controls.Add(new Label { Text = "Label:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, 2);
        grid.Controls.Add(_label, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += OnSave;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        if (scripts.Count == 0 && string.IsNullOrEmpty(source.Script))
        {
            var none = new Label
            {
                Text = "No scripts found. Use 'Open Script Folder' in the tray menu to add one, then try again.",
                AutoSize = true,
                MaximumSize = new Size(410, 0),
                ForeColor = Color.Firebrick,
            };
            grid.Controls.Add(none, 0, 3);
            grid.SetColumnSpan(none, 2);
        }

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(grid, 0, 0);
        buttons.Margin = new Padding(12, 0, 12, 12);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        AcceptButton = save;
        CancelButton = cancel;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var label = _label.Text.Trim();
        if (label.Length == 0 || label.Length > 60)
        {
            MessageBox.Show(this, "Enter a label of 1 to 60 characters.", "NFC Desk", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var script = _script.SelectedItem as string;
        if (script is null || !_library.TryResolve(script, out _, out var error))
        {
            MessageBox.Show(this, "Pick a valid script" + (script is null ? "." : $" ({error})."), "NFC Desk",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var m = _source.Clone();
        m.Label = label;
        m.Script = script;
        Result = m;
        DialogResult = DialogResult.OK;
        Close();
    }
}

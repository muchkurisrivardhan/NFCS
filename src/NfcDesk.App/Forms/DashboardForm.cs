using System.Runtime.Versioning;
using NfcDesk.Core;
using NfcDesk.Core.Config;
using NfcDesk.Core.Logging;
using NfcDesk.Core.Scripts;

namespace NfcDesk.App.Forms;

[SupportedOSPlatform("windows")]
public sealed class DashboardForm : Form
{
    private readonly ConfigStore _config;
    private readonly RollingFileLog _log;
    private readonly ScriptLibrary _library;
    private readonly ScriptRunner _runner;
    private readonly AgentState _state;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
    };
    private readonly ListView _activity = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };
    private readonly Label _status = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly Label _statusLine = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly Button _edit = new() { Text = "Edit", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true };
    private readonly Button _test = new() { Text = "Test run", AutoSize = true };

    public DashboardForm(ConfigStore config, RollingFileLog log, ScriptLibrary library, ScriptRunner runner, AgentState state)
    {
        _config = config;
        _log = log;
        _library = library;
        _runner = runner;
        _state = state;

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Text = "NFC Desk";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        ClientSize = new Size(820, 580);

        _grid.Columns.Add("Label", "Label");
        _grid.Columns.Add("Tag", "Tag ID");
        _grid.Columns.Add("Script", "Script");
        _grid.Columns.Add("Last", "Last tapped");
        _grid.Columns.Add("Count", "Tap count");
        _grid.Columns[4].FillWeight = 40;
        _grid.SelectionChanged += (_, _) => UpdateButtons();

        _activity.Columns.Add("Time", 150);
        _activity.Columns.Add("Source IP", 110);
        _activity.Columns.Add("Tag", 160);
        _activity.Columns.Add("Result", 70);
        _activity.Columns.Add("Reason", 120);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
        buttons.Controls.AddRange(new Control[] { _edit, _remove, _test, _status });
        _edit.Click += (_, _) => EditSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _test.Click += async (_, _) => await TestSelectedAsync();

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(_grid, 0, 0);
        top.Controls.Add(buttons, 0, 1);

        var activityBox = new GroupBox { Text = "Recent activity", Dock = DockStyle.Fill, Padding = new Padding(6) };
        activityBox.Controls.Add(_activity);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(activityBox);
        split.Panel1MinSize = 150;
        split.Panel2MinSize = 120;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(split, 0, 0);
        root.Controls.Add(_statusLine, 0, 1);
        Controls.Add(root);

        _config.Changed += OnConfigChanged;
        _log.TapLogged += OnTapLogged;
        _state.Changed += OnStateChanged;
        FormClosed += (_, _) =>
        {
            _config.Changed -= OnConfigChanged;
            _log.TapLogged -= OnTapLogged;
            _state.Changed -= OnStateChanged;
        };

        RefreshGrid();
        foreach (var e in _log.Recent().Reverse()) AddActivity(e);
        UpdateStatusLine();
        UpdateButtons();
    }

    private void Marshal(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    private void OnConfigChanged() => Marshal(() => { RefreshGrid(); UpdateStatusLine(); });
    private void OnTapLogged(TapLogEntry e) => Marshal(() => AddActivity(e));
    private void OnStateChanged() => Marshal(UpdateStatusLine);

    private void UpdateStatusLine()
    {
        var port = _config.Snapshot().Port;
        var s = $"Listening on port {port}";
        if (_state.Paused) s += " · Paused";
        if (_state.LearningMode) s += " · Learning a new tag";
        _statusLine.Text = s;
    }

    private void UpdateButtons()
    {
        bool has = _grid.SelectedRows.Count > 0;
        _edit.Enabled = _remove.Enabled = _test.Enabled = has;
    }

    private string? SelectedTag() =>
        _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as string : null;

    private void RefreshGrid()
    {
        var selected = SelectedTag();
        _grid.Rows.Clear();
        foreach (var t in _config.Snapshot().Tags)
        {
            var last = t.LastTapped is { } l ? l.ToLocalTime().ToString("g") : "never";
            int i = _grid.Rows.Add(t.Label, t.Tag, t.Script, last, t.TapCount);
            _grid.Rows[i].Tag = t.Tag;
            if (t.Tag == selected) _grid.Rows[i].Selected = true;
        }
        UpdateButtons();
    }

    private void AddActivity(TapLogEntry e)
    {
        var item = new ListViewItem(new[]
        {
            e.Time.ToLocalTime().ToString("g"),
            e.Source?.ToString() ?? "",
            e.Tag ?? "",
            e.Accepted ? "ok" : "denied",
            e.Reason == DenyReason.None ? "" : e.Reason.ToString(),
        });
        _activity.Items.Insert(0, item);
        while (_activity.Items.Count > 200) _activity.Items.RemoveAt(_activity.Items.Count - 1);
    }

    private void EditSelected()
    {
        var tag = SelectedTag();
        var mapping = tag is null ? null : _config.FindTag(tag);
        if (mapping is null) return;
        using var dlg = new LearnTagForm(_library, mapping, editMode: true);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result is { } r)
            _config.UpsertTag(r);
    }

    private void RemoveSelected()
    {
        var tag = SelectedTag();
        var mapping = tag is null ? null : _config.FindTag(tag);
        if (mapping is null) return;
        var r = MessageBox.Show(this, $"Remove '{mapping.Label}'? Tapping this tag will no longer run anything.",
            "Remove tag", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r == DialogResult.Yes) _config.RemoveTag(mapping.Tag);
    }

    private async Task TestSelectedAsync()
    {
        var tag = SelectedTag();
        var mapping = tag is null ? null : _config.FindTag(tag);
        if (mapping is null) return;
        _test.Enabled = false;
        _status.Text = $"Running {mapping.Script}…";
        try
        {
            var result = await _runner.RunAsync(mapping.Script);
            if (IsDisposed) return;
            _status.Text = !result.Started ? "Could not start script (see log)"
                : result.TimedOut ? "Timed out and was stopped"
                : result.ExitCode is { } c ? (c == 0 ? "Finished OK (exit code 0)" : $"Finished with exit code {c}")
                : "Launched";
        }
        catch (Exception ex)
        {
            _log.Error("Test run failed", ex);
            if (!IsDisposed) _status.Text = "Test run failed (see log)";
        }
        finally
        {
            if (!IsDisposed) UpdateButtons();
        }
    }
}

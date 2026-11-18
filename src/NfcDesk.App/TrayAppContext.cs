using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NfcDesk.App.Forms;
using NfcDesk.App.SystemIntegration;
using NfcDesk.Core;
using NfcDesk.Core.Config;
using NfcDesk.Core.Logging;
using NfcDesk.Core.Net;
using NfcDesk.Core.Protocol;
using NfcDesk.Core.Scripts;
using NfcDesk.Core.Server;

namespace NfcDesk.App;

[SupportedOSPlatform("windows")]
public sealed class TrayAppContext : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly SynchronizationContext _ui;
    private readonly AppPaths _paths;
    private readonly ConfigStore _config;
    private readonly RollingFileLog _log;
    private readonly DpapiSecretStore _secrets;
    private readonly AgentState _state;
    private readonly ScriptLibrary _library;
    private readonly ScriptRunner _runner;
    private readonly TapServer _server;

    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly IntPtr _iconHandle;

    private readonly ToolStripMenuItem _miLearn;
    private readonly ToolStripMenuItem _miPause;
    private readonly ToolStripMenuItem _miNotify;
    private readonly ToolStripMenuItem _miStartup;

    private readonly System.Threading.Timer _netDebounce;
    private readonly System.Threading.Timer _publicTimer;

    private DashboardForm? _dashboard;
    private PairForm? _pairForm;
    private bool _publicNetwork;
    private bool _listening;
    private bool _quitting;
    // UI thread only. Set when a learn candidate is taken; reset when Learning Mode is started again.
    // Guarantees at most one tag is learned per Learning Mode session even if several signed
    // unknown-tag taps were accepted before learning was switched off.
    private bool _learnConsumed;

    public TrayAppContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _paths = AppPaths.Default;
        _paths.EnsureCreated();
        bool firstRun = !File.Exists(_paths.ConfigFile);

        _config = new ConfigStore(_paths);
        _log = new RollingFileLog(_paths.LogsDir);
        Program.ErrorSink = (msg, ex) => { try { _log.Error(msg, ex); } catch { } };
        _secrets = new DpapiSecretStore(_paths, _log);
        _state = new AgentState(_config);
        _library = new ScriptLibrary(_paths.ScriptsDir);
        _runner = new ScriptRunner(_library, _log);
        var verifier = new TapVerifier(_secrets, _config, _state);
        _server = new TapServer(verifier, new RateLimiter(10), _log);

        if (firstRun) CopyStarterScripts();

        // Tray icon + menu
        _iconHandle = CreateIconHandle();
        _icon = Icon.FromHandle(_iconHandle);

        _miLearn = new ToolStripMenuItem("Pair New Tag (Learning Mode)");
        _miLearn.Click += (_, _) => ToggleLearning();
        _miPause = new ToolStripMenuItem("Pause");
        _miPause.Click += (_, _) => _state.Paused = !_state.Paused;
        _miNotify = new ToolStripMenuItem("Show Notifications") { CheckOnClick = true, Checked = _config.Snapshot().Notifications };
        _miNotify.Click += (_, _) => { bool v = _miNotify.Checked; _config.Update(c => c.Notifications = v); };
        _miStartup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        try { _miStartup.Checked = StartupRegistration.IsEnabled(); } catch (Exception ex) { _log.Error("Startup check failed", ex); }
        _miStartup.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(_miStartup.Checked); }
            catch (Exception ex)
            {
                _log.Error("Could not change startup setting", ex);
                try { _miStartup.Checked = StartupRegistration.IsEnabled(); } catch { }
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(Item("Open Dashboard", ShowDashboard));
        menu.Items.Add(Item("Pair Phone", ShowPair));
        menu.Items.Add(_miLearn);
        menu.Items.Add(_miPause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Open Script Folder", OpenScriptFolder));
        menu.Items.Add(Item("View Log", ViewLog));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_miNotify);
        menu.Items.Add(_miStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Quit", Quit));

        _tray = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = menu,
            Visible = true,
            Text = "NFC Desk",
        };
        _tray.DoubleClick += (_, _) => ShowDashboard();

        _config.Changed += () => _ui.Post(_ => SyncNotifyMenu(), null);
        _state.Changed += () => _ui.Post(_ => RefreshStateUi(), null);
        _server.TapAccepted += OnTapAccepted;

        _netDebounce = new System.Threading.Timer(_ => _ui.Post(async _ => await OnNetworkSettledAsync(), null), null,
            Timeout.Infinite, Timeout.Infinite);
        _publicTimer = new System.Threading.Timer(_ => _ui.Post(async _ => await RefreshPublicAsync(), null), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        RefreshStateUi();
        _log.Info("NFC Desk started");

        _ = StartupAsync(firstRun);
    }

    private static ToolStripMenuItem Item(string text, Action onClick)
    {
        var i = new ToolStripMenuItem(text);
        i.Click += (_, _) => onClick();
        return i;
    }

    // ---------- startup ----------

    private async Task StartupAsync(bool firstRun)
    {
        try
        {
            await StartServerAsync();
            await RefreshPublicAsync();
            if (!_config.Snapshot().FirewallPrompted) await MaybePromptFirewallAsync();
        }
        catch (Exception ex) { _log.Error("Startup failed", ex); }
    }

    private void CopyStarterScripts()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            const string prefix = "StarterScripts.";
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var dest = Path.Combine(_paths.ScriptsDir, name.Substring(prefix.Length));
                if (File.Exists(dest)) continue;
                using var s = asm.GetManifestResourceStream(name);
                if (s is null) continue;
                using var f = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);
                s.CopyTo(f);
            }
        }
        catch (Exception ex) { _log.Error("Could not copy starter scripts", ex); }
    }

    private async Task MaybePromptFirewallAsync()
    {
        int port = _config.Snapshot().Port;
        try
        {
            if (!await FirewallHelper.RuleExistsAsync(port))
            {
                var r = MessageBox.Show(
                    $"Allow your iPhone to reach NFC Desk?\n\nNFC Desk can add a Windows Firewall rule allowing inbound TCP port {port} on PRIVATE networks only. Windows will ask for administrator permission once.",
                    "NFC Desk", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes)
                {
                    bool ok = await FirewallHelper.AddPrivateRuleElevatedAsync(port);
                    Balloon(ok ? "Firewall rule added" : "Firewall rule was not added", force: true);
                }
            }
        }
        catch (Exception ex) { _log.Error("Firewall setup failed", ex); }
        finally { _config.Update(c => c.FirewallPrompted = true); }
    }

    // ---------- server ----------

    private async Task StartServerAsync()
    {
        int port = _config.Snapshot().Port;
        var addrs = new List<IPAddress>(LocalAddresses.GetLanIPv4()) { IPAddress.Loopback };
        try
        {
            await _server.StartAsync(port, addrs);
            _listening = true;
            _log.Info($"Listening on port {port}");
        }
        catch (Exception ex)
        {
            _listening = false;
            _log.Error($"Could not listen on port {port}", ex);
            Balloon($"NFC Desk couldn't listen on port {port}", force: true);
        }
        _ui.Post(_ => RefreshStateUi(), null);
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        try { _netDebounce.Change(3000, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }

    private async Task OnNetworkSettledAsync()
    {
        if (_quitting) return;
        try
        {
            await StartServerAsync();
            await RefreshPublicAsync();
        }
        catch (Exception ex) { _log.Error("Network change handling failed", ex); }
    }

    private async Task RefreshPublicAsync()
    {
        try { _publicNetwork = await NetworkCategory.IsAnyPublicAsync(); }
        catch (Exception ex) { _log.Error("Public network check failed", ex); }
        RefreshStateUi();
    }

    // ---------- taps ----------

    private void OnTapAccepted(TapVerdict verdict)
    {
        // Worker thread: switch learning off immediately so later taps aren't learned, then marshal to the UI thread.
        if (verdict.LearnCandidate) _state.StopLearning();
        _ui.Post(_ => HandleTap(verdict), null);
    }

    private void HandleTap(TapVerdict verdict)
    {
        if (_quitting || verdict.Tag is null) return;
        try
        {
            if (verdict.LearnCandidate)
            {
                _state.StopLearning();
                if (_learnConsumed) { _log.Info($"Ignored extra learn candidate '{verdict.Tag}'"); return; }
                _learnConsumed = true;
                var mapping = new TagMapping { Tag = verdict.Tag, Label = verdict.Tag };
                using var dlg = new LearnTagForm(_library, mapping, editMode: false);
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result is { } r)
                {
                    _config.UpsertTag(r);
                    Balloon($"Learned tag ‘{r.Label}’");
                }
                return;
            }

            _config.RecordTap(verdict.Tag, DateTimeOffset.Now);
            var m = _config.FindTag(verdict.Tag);
            if (m is null) return;
            _ = RunScriptAsync(m);
        }
        catch (Exception ex) { _log.Error("Tap handling failed", ex); }
    }

    private async Task RunScriptAsync(TagMapping m)
    {
        try
        {
            var result = await _runner.RunAsync(m.Script);
            Balloon(result.Started ? $"Ran: {m.Label}" : $"Failed to run: {m.Label}");
        }
        catch (Exception ex) { _log.Error($"Running {m.Script} failed", ex); }
    }

    // ---------- UI ----------

    private void Balloon(string text, bool force = false)
    {
        _ui.Post(_ =>
        {
            if (_quitting) return;
            if (!force && !_config.Snapshot().Notifications) return;
            try { _tray.ShowBalloonTip(3000, "NFC Desk", text, ToolTipIcon.Info); } catch { }
        }, null);
    }

    private void SyncNotifyMenu() => _miNotify.Checked = _config.Snapshot().Notifications;

    private void RefreshStateUi()
    {
        if (_quitting) return;
        bool learning = _state.LearningMode;
        _miLearn.Checked = learning;
        _miPause.Text = _state.Paused ? "Resume" : "Pause";

        int port = _config.Snapshot().Port;
        string text;
        if (_publicNetwork) text = "⚠ Public network: taps will be blocked. Set this Wi-Fi to Private.";
        else if (_state.Paused) text = "NFC Desk — paused";
        else if (learning) text = "NFC Desk — learning a new tag…";
        else if (!_listening) text = $"NFC Desk — not listening on port {port}";
        else text = $"NFC Desk — listening on port {port}";
        if (text.Length > 127) text = text.Substring(0, 127);
        _tray.Text = text;
    }

    private void ToggleLearning()
    {
        if (_state.LearningMode) _state.StopLearning();
        else { _learnConsumed = false; _state.StartLearning(); }
    }

    private void ShowDashboard()
    {
        if (_dashboard is { IsDisposed: false })
        {
            if (_dashboard.WindowState == FormWindowState.Minimized) _dashboard.WindowState = FormWindowState.Normal;
            _dashboard.Activate();
            return;
        }
        _dashboard = new DashboardForm(_config, _log, _library, _runner, _state);
        _dashboard.FormClosed += (_, _) => _dashboard = null;
        _dashboard.Show();
        _dashboard.Activate();
    }

    private void ShowPair()
    {
        if (_pairForm is { IsDisposed: false })
        {
            _pairForm.Activate();
            return;
        }
        _pairForm = new PairForm(_secrets, _config.Snapshot().Port);
        _pairForm.FormClosed += (_, _) => _pairForm = null;
        _pairForm.Show();
        _pairForm.Activate();
    }

    private void OpenScriptFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.ScriptsDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _log.Error("Could not open script folder", ex); }
    }

    private void ViewLog()
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_log.CurrentFile}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _log.Error("Could not open log", ex); }
    }

    private void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _log.Info("NFC Desk quitting");
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _tray.Visible = false;
        try { _dashboard?.Close(); } catch { }
        try { _pairForm?.Close(); } catch { }
        try { Task.Run(() => _server.StopAsync()).Wait(TimeSpan.FromSeconds(5)); } catch { }
        Cleanup();
        ExitThread();
    }

    private void Cleanup()
    {
        _netDebounce.Dispose();
        _publicTimer.Dispose();
        _state.Dispose();
        _tray.Dispose();
        _icon.Dispose();
        DestroyIcon(_iconHandle);
        _log.Dispose();
    }

    private static IntPtr CreateIconHandle()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            const int r = 8;
            var rect = new Rectangle(1, 1, 30, 30);
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x1E, 0x6F, 0xD9));
            g.FillPath(fill, path);
            using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("N", font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
        }
        return bmp.GetHicon();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}

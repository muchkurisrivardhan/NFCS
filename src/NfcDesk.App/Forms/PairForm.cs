using System.Net;
using System.Runtime.Versioning;
using NfcDesk.App.SystemIntegration;
using NfcDesk.Core.Net;
using QRCoder;

namespace NfcDesk.App.Forms;

[SupportedOSPlatform("windows")]
public sealed class PairForm : Form
{
    private readonly DpapiSecretStore _store;
    private readonly int _port;
    private readonly TextBox _secretBox = new() { ReadOnly = true, Font = new Font("Consolas", 9f), Dock = DockStyle.Fill };
    private readonly PictureBox _qr = new() { Size = new Size(200, 200), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.Top };
    private readonly FlowLayoutPanel _urls = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _publicWarning = new()
    {
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Visible = false,
        MaximumSize = new Size(480, 0),
        Text = "Warning: a network is set to Public, so taps will be blocked. Set this Wi-Fi to Private.",
    };

    public PairForm(DpapiSecretStore store, int port)
    {
        _store = store;
        _port = port;

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Text = "Pair Phone";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(540, 620);
        ClientSize = new Size(540, 620);
        MaximizeBox = false;

        if (!_store.HasSecret) _store.Regenerate();

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
        };

        panel.Controls.Add(_publicWarning);
        panel.Controls.Add(new Label
        {
            Text = "In the iPhone shortcut ‘NFC · Send Tap to PC’, paste the secret into the Secret text and the URL into the PC text.",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 0, 0, 10),
        });

        panel.Controls.Add(new Label { Text = "Secret", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        var secretRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Width = 500, Height = 32 };
        secretRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        secretRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        secretRow.Controls.Add(_secretBox, 0, 0);
        var copySecret = new Button { Text = "Copy", AutoSize = true };
        copySecret.Click += (_, _) => CopyText(_secretBox.Text);
        secretRow.Controls.Add(copySecret, 1, 0);
        panel.Controls.Add(secretRow);

        panel.Controls.Add(new Label { Text = "QR code (secret only)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 0) });
        panel.Controls.Add(_qr);

        panel.Controls.Add(new Label { Text = "PC address", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 0) });
        panel.Controls.Add(_urls);

        var regen = new Button { Text = "Regenerate secret", AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        regen.Click += OnRegenerate;
        panel.Controls.Add(regen);

        Controls.Add(panel);

        RefreshSecret();
        BuildUrls();
        Shown += async (_, _) => await CheckPublicAsync();
    }

    private async Task CheckPublicAsync()
    {
        try
        {
            bool pub = await NetworkCategory.IsAnyPublicAsync();
            if (!IsDisposed) _publicWarning.Visible = pub;
        }
        catch { /* best effort */ }
    }

    private void RefreshSecret()
    {
        var secret = _store.GetSecret() ?? "";
        _secretBox.Text = secret;
        var old = _qr.Image;
        _qr.Image = secret.Length == 0 ? null : MakeQr(secret);
        old?.Dispose();
    }

    private static Bitmap MakeQr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        using var ms = new MemoryStream(png);
        using var img = new Bitmap(ms);
        return new Bitmap(img);
    }

    private void BuildUrls()
    {
        _urls.Controls.Clear();
        var ips = LocalAddresses.GetLanIPv4();
        if (ips.Count == 0)
        {
            _urls.Controls.Add(new Label
            {
                Text = "No private LAN address found — connect to your home Wi-Fi",
                AutoSize = true,
                ForeColor = Color.Firebrick,
            });
            return;
        }
        foreach (IPAddress ip in ips)
        {
            var url = $"http://{ip}:{_port}/tap";
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            row.Controls.Add(new Label { Text = url, AutoSize = true, Font = new Font("Consolas", 9f), Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
            var copy = new Button { Text = "Copy", AutoSize = true };
            copy.Click += (_, _) => CopyText(url);
            row.Controls.Add(copy);
            _urls.Controls.Add(row);
        }
    }

    private void OnRegenerate(object? sender, EventArgs e)
    {
        var r = MessageBox.Show(this,
            "The old secret stops working immediately; you'll need to update the shortcut on your iPhone.",
            "Regenerate secret", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (r != DialogResult.OK) return;
        _store.Regenerate();
        RefreshSecret();
    }

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (Exception) { /* clipboard busy */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _qr.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}

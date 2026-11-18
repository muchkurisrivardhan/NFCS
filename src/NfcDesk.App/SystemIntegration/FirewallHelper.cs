using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace NfcDesk.App.SystemIntegration;

/// <summary>Checks for / creates the private-profile inbound firewall rule.</summary>
[SupportedOSPlatform("windows")]
public static class FirewallHelper
{
    public static string RuleName(int port) => $"NFC Desk Agent (TCP {port})";

    private static string PsQuote(string s) => s.Replace("'", "''");

    public static async Task<bool> RuleExistsAsync(int port)
    {
        try
        {
            var name = PsQuote(RuleName(port));
            var script =
                $"$r = Get-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue | " +
                "Where-Object { $_.Enabled -eq 'True' -and $_.Direction -eq 'Inbound' -and $_.Action -eq 'Allow' -and $_.Profile -eq 'Private' }; " +
                "if ($r) { 'yes' } else { 'no' }";
            var output = await PowerShellRunner.RunAsync(script, 15000).ConfigureAwait(false);
            if (output is null) return false;
            return output.Split('\n').Any(l => l.Trim().Equals("yes", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> AddPrivateRuleElevatedAsync(int port)
    {
        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be 1..65535.");

        try
        {
            var exe = Environment.ProcessPath ?? string.Empty;
            var script = BuildAddRuleScript(port, exe);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return false;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // UAC cancelled
        }
        catch (ArgumentOutOfRangeException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    internal static string BuildAddRuleScript(int port, string exePath)
    {
        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be 1..65535.");

        var name = PsQuote(RuleName(port));
        var exe = PsQuote(exePath);
        return
            "$ErrorActionPreference = 'Stop'\n" +
            "try {\n" +
            $"  Get-NetFirewallRule -DisplayName '{name}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue\n" +
            $"  New-NetFirewallRule -DisplayName '{name}' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {port} -Profile Private -Program '{exe}' | Out-Null\n" +
            "  exit 0\n" +
            "} catch {\n" +
            "  exit 1\n" +
            "}\n";
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace NfcDesk.App.SystemIntegration;

/// <summary>Runs a hidden, non-elevated powershell.exe and captures stdout.</summary>
[SupportedOSPlatform("windows")]
internal static class PowerShellRunner
{
    /// <summary>Returns stdout, or null on timeout/any error/non-zero exit.</summary>
    public static async Task<string?> RunAsync(string script, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            using var cts = new CancellationTokenSource(timeoutMs);
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                var output = await stdoutTask.ConfigureAwait(false);
                await stderrTask.ConfigureAwait(false);
                return proc.ExitCode == 0 ? output : null;
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return null;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}

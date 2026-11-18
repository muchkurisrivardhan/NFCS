using System.Diagnostics;
using NfcDesk.Core;
using NfcDesk.Core.Scripts;

namespace NfcDesk.Tests.Scripts;

public class ScriptRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nfcdesk-run-" + Guid.NewGuid().ToString("N"));

    public ScriptRunnerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class FakeLog : IActivityLog
    {
        public List<string> Lines = new();
        public void LogTap(TapLogEntry entry) { }
        public void Info(string message) { lock (Lines) Lines.Add("I " + message); }
        public void Error(string message, Exception? ex = null) { lock (Lines) Lines.Add("E " + message); }
    }

    private static string? NoPath(string _) => null;

    [Fact]
    public void Build_Ps1()
    {
        var p = Path.Combine(_root, "a.ps1");
        var psi = ScriptRunner.BuildStartInfo(p, NoPath)!;
        Assert.Equal("powershell.exe", psi.FileName);
        Assert.Equal(new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", p }, psi.ArgumentList);
        AssertHeadless(psi);
        Assert.Equal(_root, psi.WorkingDirectory);
    }

    [Theory]
    [InlineData("a.bat")]
    [InlineData("a.CMD")]
    public void Build_BatCmd(string name)
    {
        var p = Path.Combine(_root, name);
        var psi = ScriptRunner.BuildStartInfo(p, NoPath)!;
        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal(new[] { "/d", "/c", p }, psi.ArgumentList);
        AssertHeadless(psi);
    }

    [Fact]
    public void Build_Exe()
    {
        var p = Path.Combine(_root, "a.exe");
        var psi = ScriptRunner.BuildStartInfo(p, NoPath)!;
        Assert.Equal(p, psi.FileName);
        Assert.Empty(psi.ArgumentList);
        AssertHeadless(psi);
    }

    [Fact]
    public void Build_Py_UsesPython_ThenPyLauncher_ThenNull()
    {
        var p = Path.Combine(_root, "a.py");
        var psi = ScriptRunner.BuildStartInfo(p, n => n == "python" ? "/bin/python" : null)!;
        Assert.Equal("/bin/python", psi.FileName);
        Assert.Equal(new[] { p }, psi.ArgumentList);
        AssertHeadless(psi);

        var psi2 = ScriptRunner.BuildStartInfo(p, n => n == "py" ? "/bin/py" : null)!;
        Assert.Equal("/bin/py", psi2.FileName);

        Assert.Null(ScriptRunner.BuildStartInfo(p, NoPath));
    }

    [Fact]
    public void Build_Lnk_UsesShellExecute()
    {
        var p = Path.Combine(_root, "a.lnk");
        var psi = ScriptRunner.BuildStartInfo(p, NoPath)!;
        Assert.True(psi.UseShellExecute);
        Assert.Equal(p, psi.FileName);
        Assert.Equal(ProcessWindowStyle.Hidden, psi.WindowStyle);
        Assert.False(psi.RedirectStandardOutput);
    }

    private static void AssertHeadless(ProcessStartInfo psi)
    {
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, psi.WindowStyle);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.Equal(System.Text.Encoding.UTF8, psi.StandardOutputEncoding);
    }

    [Fact]
    public async Task ResolveFailure_NotStarted()
    {
        var log = new FakeLog();
        var r = new ScriptRunner(new ScriptLibrary(_root), log);
        var res = await r.RunAsync("../evil.ps1");
        Assert.False(res.Started);
        Assert.Contains(log.Lines, l => l.StartsWith("E "));
    }

    [Fact]
    public async Task PythonMissing_LogsAndNotStarted()
    {
        File.WriteAllText(Path.Combine(_root, "a.py"), "");
        var log = new FakeLog();
        var r = new ScriptRunner(new ScriptLibrary(_root), log, null, p => ScriptRunner.BuildStartInfo(p, NoPath));
        var res = await r.RunAsync("a.py");
        Assert.False(res.Started);
        Assert.Contains(log.Lines, l => l.Contains("python not found on PATH"));
    }

    private static ProcessStartInfo Sh(string script)
    {
        var psi = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);
        return psi;
    }

    private ScriptRunner Make(string script, TimeSpan? timeout, FakeLog log)
    {
        File.WriteAllText(Path.Combine(_root, "t.ps1"), "");
        return new ScriptRunner(new ScriptLibrary(_root), log, timeout, _ => Sh(script));
    }

    [Fact]
    public async Task CapturesOutput_AndExitCode()
    {
        if (OperatingSystem.IsWindows()) return;
        var log = new FakeLog();
        var res = await Make("echo hello; echo err >&2; exit 3", null, log).RunAsync("t.ps1");
        Assert.True(res.Started);
        Assert.Equal(3, res.ExitCode);
        Assert.False(res.TimedOut);
        Assert.Contains("hello", res.Output);
        Assert.Contains("err", res.Output);
        Assert.Contains(log.Lines, l => l.Contains("exited with code 3"));
    }

    [Fact]
    public async Task Timeout_KillsProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        var log = new FakeLog();
        var sw = Stopwatch.StartNew();
        var res = await Make("exec sleep 5", TimeSpan.FromMilliseconds(300), log).RunAsync("t.ps1");
        sw.Stop();
        Assert.True(res.Started);
        Assert.True(res.TimedOut);
        Assert.Null(res.ExitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4));
        Assert.Contains(log.Lines, l => l.Contains("timed out"));
    }

    [Fact]
    public async Task Output_IsCapped()
    {
        if (OperatingSystem.IsWindows()) return;
        var res = await Make("head -c 200000 /dev/zero | tr '\\0' 'a' | fold -w 100", TimeSpan.FromSeconds(20), new FakeLog()).RunAsync("t.ps1");
        Assert.True(res.Output.Length < 70 * 1024);
        Assert.Contains("[output truncated]", res.Output);
    }
}

using System.Diagnostics;
using System.Text;

namespace NfcDesk.Core.Scripts;

public sealed record ScriptResult(bool Started, int? ExitCode, bool TimedOut, string Output);

public sealed class ScriptRunner
{
    private const int MaxOutputBytes = 64 * 1024;

    private readonly ScriptLibrary _lib;
    private readonly IActivityLog _log;
    private readonly TimeSpan _timeout;
    private readonly Func<string, ProcessStartInfo?> _builder;

    public ScriptRunner(ScriptLibrary lib, IActivityLog log, TimeSpan? timeout = null)
        : this(lib, log, timeout, null) { }

    // Test seam: custom start-info builder.
    internal ScriptRunner(ScriptLibrary lib, IActivityLog log, TimeSpan? timeout, Func<string, ProcessStartInfo?>? builder)
    {
        _lib = lib;
        _log = log;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        _builder = builder ?? (p => BuildStartInfo(p, DefaultFindOnPath));
    }

    public async Task<ScriptResult> RunAsync(string relative, CancellationToken ct = default)
    {
        if (!_lib.TryResolve(relative, out var full, out var error))
        {
            _log.Error($"Script rejected '{relative}': {error}");
            return new ScriptResult(false, null, false, "");
        }

        var psi = _builder(full);
        if (psi is null)
        {
            _log.Error($"Cannot run '{relative}': python not found on PATH");
            return new ScriptResult(false, null, false, "");
        }

        _log.Info($"Starting script {relative}");
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex)
        {
            _log.Error($"Failed to start script {relative}", ex);
            return new ScriptResult(false, null, false, "");
        }
        if (proc is null)
        {
            _log.Info($"Script {relative} launched (no process handle)");
            return new ScriptResult(true, null, false, "");
        }

        using (proc)
        {
            if (psi.UseShellExecute || !psi.RedirectStandardOutput)
            {
                _log.Info($"Script {relative} launched (not waiting)");
                return new ScriptResult(true, null, false, "");
            }

            var output = new CappedOutput(MaxOutputBytes);
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            bool timedOut = false;
            try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                try { proc.Kill(entireProcessTree: false); } catch { }
                try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
                if (!timedOut) throw;
            }

            // Ensure async output handlers have flushed.
            if (!timedOut)
            {
                try { proc.WaitForExit(); } catch { }
            }

            var text = output.ToString();
            if (timedOut)
            {
                _log.Error($"Script {relative} timed out after {_timeout.TotalSeconds:0.#}s and was killed");
                if (text.Length > 0) _log.Info($"Script {relative} output: {text}");
                return new ScriptResult(true, null, true, text);
            }

            int code = proc.ExitCode;
            _log.Info($"Script {relative} exited with code {code}");
            if (text.Length > 0) _log.Info($"Script {relative} output: {text}");
            return new ScriptResult(true, code, false, text);
        }
    }

    public static ProcessStartInfo? BuildStartInfo(string fullPath, Func<string, string?> findOnPath)
    {
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        ProcessStartInfo psi;
        switch (ext)
        {
            case ".lnk":
                return new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = dir,
                };
            case ".ps1":
                psi = new ProcessStartInfo("powershell.exe");
                foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", fullPath }) psi.ArgumentList.Add(a);
                break;
            case ".bat":
            case ".cmd":
                psi = new ProcessStartInfo("cmd.exe");
                foreach (var a in new[] { "/d", "/c", fullPath }) psi.ArgumentList.Add(a);
                break;
            case ".exe":
                psi = new ProcessStartInfo(fullPath);
                break;
            case ".py":
                var py = findOnPath("python") ?? findOnPath("py");
                if (py is null) return null;
                psi = new ProcessStartInfo(py);
                psi.ArgumentList.Add(fullPath);
                break;
            default:
                return null;
        }
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.WorkingDirectory = dir;
        return psi;
    }

    public static string? DefaultFindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var exts = new List<string> { "" };
        if (OperatingSystem.IsWindows())
        {
            var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            exts.Clear();
            exts.AddRange(pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var e in exts)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), name + e);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    private sealed class CappedOutput(int maxBytes)
    {
        private readonly StringBuilder _sb = new();
        private readonly object _l = new();
        private int _bytes;
        private bool _truncated;

        public void AppendLine(string line)
        {
            lock (_l)
            {
                if (_truncated) return;
                var n = Encoding.UTF8.GetByteCount(line) + 1;
                if (_bytes + n > maxBytes)
                {
                    _truncated = true;
                    _sb.Append("[output truncated]\n");
                    return;
                }
                _bytes += n;
                _sb.Append(line).Append('\n');
            }
        }

        public override string ToString() { lock (_l) return _sb.ToString(); }
    }
}

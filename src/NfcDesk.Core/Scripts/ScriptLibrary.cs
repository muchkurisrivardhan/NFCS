namespace NfcDesk.Core.Scripts;

public sealed class ScriptLibrary(string scriptsDir)
{
    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(new[] { ".ps1", ".bat", ".cmd", ".exe", ".lnk", ".py" }, StringComparer.OrdinalIgnoreCase);

    // cmd.exe metacharacters: rejected so .bat/.cmd invocation can never be abused.
    private static readonly char[] CmdMeta = { '&', '|', '<', '>', '^', '%', '"' };

    public string Root { get; } = Path.GetFullPath(scriptsDir);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool TryResolve(string? relative, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";
        if (string.IsNullOrWhiteSpace(relative)) { error = "empty script path"; return false; }
        if (relative.Contains('\0') || relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        { error = "invalid characters in script path"; return false; }
        if (relative.Contains(':')) { error = "':' not allowed in script path"; return false; }
        if (relative.IndexOfAny(CmdMeta) >= 0) { error = "shell metacharacters not allowed in script path"; return false; }
        if (relative[0] == '/' || relative[0] == '\\' || Path.IsPathRooted(relative))
        { error = "absolute paths not allowed"; return false; }
        var segments = relative.Split('/', '\\');
        if (segments.Any(s => s == ".."))
        { error = "'..' not allowed in script path"; return false; }
        if (!AllowedExtensions.Contains(Path.GetExtension(relative)))
        { error = "script extension not allowed"; return false; }

        string full;
        try { full = Path.GetFullPath(Path.Combine(Root, relative)); }
        catch (Exception ex) { error = "invalid path: " + ex.Message; return false; }

        var prefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison)) { error = "path escapes script library"; return false; }
        if (!File.Exists(full)) { error = "script not found"; return false; }

        // Walk from the file up to and including root; reject any link.
        var rootTrim = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        FileSystemInfo cur = new FileInfo(full);
        try
        {
            while (true)
            {
                if (IsLink(cur)) { error = "symlinks/junctions are not allowed"; return false; }
                var curFull = cur.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(curFull, rootTrim, PathComparison)) break;
                var parent = cur is FileInfo fi ? fi.Directory : ((DirectoryInfo)cur).Parent;
                if (parent is null) { error = "path escapes script library"; return false; }
                cur = parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { error = "cannot inspect script path: " + ex.Message; return false; }

        fullPath = full;
        return true;
    }

    private static bool IsLink(FileSystemInfo i) =>
        (i.Attributes & FileAttributes.ReparsePoint) != 0 || i.LinkTarget is not null;

    public IReadOnlyList<string> ListScripts()
    {
        var result = new List<string>();
        if (!Directory.Exists(Root)) return result;
        if (IsLink(new DirectoryInfo(Root))) return result;
        Walk(new DirectoryInfo(Root), "", result);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static void Walk(DirectoryInfo dir, string prefix, List<string> result)
    {
        try
        {
            foreach (var f in dir.EnumerateFiles())
            {
                if (IsLink(f) || !AllowedExtensions.Contains(f.Extension)) continue;
                result.Add(prefix + f.Name);
            }
            foreach (var d in dir.EnumerateDirectories())
            {
                if (IsLink(d)) continue;
                Walk(d, prefix + d.Name + "/", result);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

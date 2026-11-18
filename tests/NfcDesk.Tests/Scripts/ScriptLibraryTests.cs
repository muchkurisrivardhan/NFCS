using NfcDesk.Core.Scripts;

namespace NfcDesk.Tests.Scripts;

public class ScriptLibraryTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "nfcdesk-lib-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _outside;
    private readonly ScriptLibrary _lib;

    public ScriptLibraryTests()
    {
        _root = Path.Combine(_base, "scripts");
        _outside = Path.Combine(_base, "outside");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_root, "ok.ps1"), "x");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "deep.py"), "x");
        File.WriteAllText(Path.Combine(_outside, "x.ps1"), "x");
        File.WriteAllText(Path.Combine(_base, "x.ps1"), "x");
        _lib = new ScriptLibrary(_root);
    }

    public void Dispose() { try { Directory.Delete(_base, true); } catch { } }

    [Theory]
    [InlineData("../x.ps1")]
    [InlineData("sub/../../x.ps1")]
    [InlineData("..\\x.ps1")]
    [InlineData("sub\\..\\..\\x.ps1")]
    [InlineData("/etc/x.ps1")]
    [InlineData("\\\\server\\share\\x.ps1")]
    [InlineData("C:\\x.ps1")]
    [InlineData("C:x.ps1")]
    [InlineData("x.ps1:ads")]
    [InlineData("ok.ps1:Zone.Identifier")]
    [InlineData("notes.txt")]
    [InlineData("missing.ps1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok\0.ps1")]
    [InlineData("%2e%2e/x.ps1")]
    [InlineData("a&b.bat")]
    [InlineData("a|b.bat")]
    [InlineData("a<b.bat")]
    [InlineData("a>b.bat")]
    [InlineData("a^b.bat")]
    [InlineData("a%PATH%.bat")]
    [InlineData("a\"b.bat")]
    public void Rejects(string rel)
    {
        Assert.False(_lib.TryResolve(rel, out var full, out var err));
        Assert.Equal("", full);
        Assert.NotEqual("", err);
    }

    [Fact]
    public void Null_Rejected() => Assert.False(_lib.TryResolve(null, out _, out _));

    [Fact]
    public void Accepts_ValidFile()
    {
        Assert.True(_lib.TryResolve("ok.ps1", out var full, out _));
        Assert.Equal(Path.Combine(_root, "ok.ps1"), full);
    }

    [Fact]
    public void Accepts_Subfolder_AndUppercaseExtension()
    {
        File.WriteAllText(Path.Combine(_root, "sub", "Up.PS1"), "x");
        Assert.True(_lib.TryResolve("sub/deep.py", out _, out _));
        Assert.True(_lib.TryResolve("sub\\deep.py", out _, out _) || !OperatingSystem.IsWindows() == true);
        Assert.True(_lib.TryResolve("sub/Up.PS1", out _, out _));
    }

    [Fact]
    public void Rejects_SymlinkedFile_PointingOutside()
    {
        try { File.CreateSymbolicLink(Path.Combine(_root, "link.ps1"), Path.Combine(_outside, "x.ps1")); }
        catch (Exception) when (OperatingSystem.IsWindows()) { return; } // needs privilege on Windows
        Assert.False(_lib.TryResolve("link.ps1", out _, out var err));
        Assert.Contains("symlink", err);
    }

    [Fact]
    public void Rejects_SymlinkedDirectory_PointingOutside()
    {
        try { Directory.CreateSymbolicLink(Path.Combine(_root, "linkdir"), _outside); }
        catch (Exception) when (OperatingSystem.IsWindows()) { return; }
        Assert.False(_lib.TryResolve("linkdir/x.ps1", out _, out var err));
        Assert.Contains("symlink", err);
    }

    [Fact]
    public void Rejects_WhenRootItselfIsSymlink()
    {
        var linkRoot = Path.Combine(_base, "rootlink");
        try { Directory.CreateSymbolicLink(linkRoot, _root); }
        catch (Exception) when (OperatingSystem.IsWindows()) { return; }
        var lib = new ScriptLibrary(linkRoot);
        Assert.False(lib.TryResolve("ok.ps1", out _, out _));
        Assert.Empty(lib.ListScripts());
    }

    [Fact]
    public void ListScripts_FiltersSortsAndSkipsLinks()
    {
        try
        {
            File.CreateSymbolicLink(Path.Combine(_root, "link.ps1"), Path.Combine(_outside, "x.ps1"));
            Directory.CreateSymbolicLink(Path.Combine(_root, "linkdir"), _outside);
        }
        catch (Exception) when (OperatingSystem.IsWindows()) { }
        Assert.Equal(new[] { "ok.ps1", "sub/deep.py" }, _lib.ListScripts());
    }

    [Fact]
    public void AllowedExtensions_AreExpected()
    {
        Assert.Equal(6, ScriptLibrary.AllowedExtensions.Count);
        Assert.Contains(".PS1", ScriptLibrary.AllowedExtensions);
    }
}

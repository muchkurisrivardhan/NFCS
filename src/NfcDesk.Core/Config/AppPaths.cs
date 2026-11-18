namespace NfcDesk.Core.Config;

public sealed class AppPaths(string root)
{
    public static AppPaths Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NFCDesk"));

    public string Root { get; } = root;
    public string ConfigFile => Path.Combine(Root, "config.json");
    public string SecretFile => Path.Combine(Root, "secret.bin");
    public string ScriptsDir => Path.Combine(Root, "scripts");
    public string LogsDir => Path.Combine(Root, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ScriptsDir);
        Directory.CreateDirectory(LogsDir);
    }
}

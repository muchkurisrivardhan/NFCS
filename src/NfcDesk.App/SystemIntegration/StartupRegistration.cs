using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NfcDesk.App.SystemIntegration;

/// <summary>Per-user (HKCU) start-with-Windows registration. Never needs admin.</summary>
[SupportedOSPlatform("windows")]
public static class StartupRegistration
{
    public const string ValueName = "NFCDesk";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string value) return false;
            return string.Equals(value.Trim().Trim('"'), exe.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the current executable path.");
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

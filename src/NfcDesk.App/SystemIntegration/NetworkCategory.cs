using System.Runtime.Versioning;

namespace NfcDesk.App.SystemIntegration;

/// <summary>Detects whether any connected network is classified Public.</summary>
[SupportedOSPlatform("windows")]
public static class NetworkCategory
{
    public static async Task<bool> IsAnyPublicAsync()
    {
        try
        {
            var output = await PowerShellRunner.RunAsync(
                "Get-NetConnectionProfile | ForEach-Object { $_.NetworkCategory.ToString() }", 15000)
                .ConfigureAwait(false);
            if (output is null) return false;
            return output.Split('\n').Any(l => l.Trim().Equals("Public", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }
}

using System.Runtime.Versioning;

namespace NfcDesk.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    internal static Action<string, Exception?>? ErrorSink;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\NFCDesk.Agent", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("NFC Desk is already running in the system tray.", "NFC Desk",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorSink?.Invoke("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorSink?.Invoke("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorSink?.Invoke("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}

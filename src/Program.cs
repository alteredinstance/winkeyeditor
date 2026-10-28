namespace WinKeyEditor;

static class Program
{
    const string MutexName = @"Local\WinKeyEditor.SingleInstance";
    const string ShowEventName = @"Local\WinKeyEditor.Show";

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            // Already running: ask the existing instance to show its window.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                using (existing) existing.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash((Exception)e.ExceptionObject);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var form = new MainForm(startInTray: args.Contains("--tray"));

        var wait = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => { if (form.IsHandleCreated) form.BeginInvoke(form.ShowWindow); },
            null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(form);
        wait.Unregister(null);
    }

    static void ReportCrash(Exception ex)
    {
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinKeyEditor", "error.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, $"[{DateTime.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
        MessageBox.Show($"WinKey Editor hit an error:\n\n{ex.Message}\n\nDetails were written to {log}",
            "WinKey Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

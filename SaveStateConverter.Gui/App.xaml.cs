using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SaveStateConverter.Gui;

public partial class App : Application
{
    /// <summary>A save-state folder given on the command line (or dropped on the executable).</summary>
    public static string? StartupFolder { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--self-check"])
            Environment.Exit(SelfCheck());
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Record(args.ExceptionObject as Exception);
        if (e.Args.Length > 0 && Directory.Exists(e.Args[0]))
            StartupFolder = Path.GetFullPath(e.Args[0]);
        base.OnStartup(e);
    }

    /// <summary>
    /// --self-check, used by tools/Package-Release.ps1. Exits without a window: 0 = catalog loaded and
    /// converter ran, 1 = catalog or titles missing, 2 = converter missing, 3 = converter failed.
    /// </summary>
    private static int SelfCheck()
    {
        try
        {
            if (Catalog.Load().Routes.Count == 0 || GameTitles.Count == 0) return 1;
        }
        catch (Exception)
        {
            return 1;
        }
        if (NativeConverter.Locate() is not { } native) return 2;
        var start = new ProcessStartInfo(native.ExecutablePath, "formats")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start);
        if (process is null) return 3;
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            return 3;
        }
        return process.ExitCode == 0 ? 0 : 3;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = Record(e.Exception);
        MessageBox.Show($"{e.Exception.Message}\n\nDetails were written to {path}", "PCSX2 SaveState Converter",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }

    private static string Record(Exception? exception)
    {
        var path = Path.Combine(Path.GetTempPath(), "PCSX2-SaveState-Converter-error.txt");
        try
        {
            File.WriteAllText(path, $"{DateTime.Now:O}\n{exception}");
        }
        catch (IOException)
        {
        }
        return path;
    }
}

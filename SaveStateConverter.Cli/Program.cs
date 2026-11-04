using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SaveStateConverter;

public static class Program
{
    /// <summary>File name of the desktop app, next to this tool or one folder up (release layout: cli\).</summary>
    public const string DesktopAppName = "PCSX2 SaveState Converter.exe";
    private const string Usage =
        "Usage:\n" +
        "  saveconvert formats\n" +
        "  saveconvert inspect <savestate.p2s>\n" +
        "  saveconvert scan <directory>\n" +
        "  saveconvert convert <input.p2s> --target <stable-version> (--output <output.p2s> | --in-place [--backup-dir <dir>]) [--native <exe>] [--verbose]\n" +
        "  saveconvert convert-dir <input-dir> --target <stable-version> (--output <output-dir> | --in-place [--backup-dir <dir>]) [--recursive] [--native <exe>]\n" +
        "  saveconvert backups [--backup-dir <dir>]\n" +
        "  saveconvert restore <backup-set-folder> [--output <dir>]\n" +
        "\n" +
        "--in-place replaces each converted state in its folder after copying the original into a new backup\n" +
        "set (default: Documents\\PCSX2 SaveState Converter\\backups). 'restore' puts the originals back.";

    public static int Main(string[] args)
    {
        try
        {
            // Double-clicked in Explorer: a console window would flash and close. Open the app instead.
            if (args.Length == 0 && StartedFromExplorer()) return OpenDesktopApp();
            if (args.Length == 0) return UsageError();
            var options = Options.Parse(args.Skip(1).ToArray());
            return args[0] switch
            {
                "formats" when options.Positional.Count == 0 => Formats(),
                "inspect" when options.Positional.Count == 1 => Inspect(options.Positional[0]),
                "scan" when options.Positional.Count == 1 => Scan(options.Positional[0]),
                "convert" when options.Positional.Count == 1 && options.Target is not null && options.HasOneDestination =>
                    options.InPlace
                        ? ConvertInPlace(options.Positional[0], options.Target, options)
                        : Convert(options.Positional[0], options.Target, options.Output!, options),
                "convert-dir" when options.Positional.Count == 1 && options.Target is not null && options.HasOneDestination =>
                    options.InPlace
                        ? ConvertDirectoryInPlace(options.Positional[0], options.Target, options)
                        : ConvertDirectory(options.Positional[0], options.Target, options.Output!, options),
                "backups" when options.Positional.Count == 0 => ListBackups(options),
                "restore" when options.Positional.Count == 1 => Restore(options.Positional[0], options.Output),
                _ => UsageError(),
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);

    /// <summary>True when this process owns its console alone, i.e. it was not started from a terminal.</summary>
    private static bool StartedFromExplorer()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected) return false;
        return GetConsoleProcessList(new uint[2], 2) == 1;
    }

    private static int OpenDesktopApp()
    {
        var app = new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "..") }
            .Select(folder => Path.GetFullPath(Path.Combine(folder, DesktopAppName)))
            .FirstOrDefault(File.Exists);
        if (app is not null)
        {
            Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            return 0;
        }
        Console.WriteLine("This is the command-line version of PCSX2 SaveState Converter; run it from a terminal (cmd or PowerShell).");
        Console.WriteLine();
        Console.WriteLine(Usage);
        Console.WriteLine();
        Console.Write("Press Enter to close.");
        Console.ReadLine();
        return 2;
    }

    private static int Formats()
    {
        Console.WriteLine($"Native converter: {NativeConverter.Locate()?.ExecutablePath ?? "not found (set SAVECONVERT_NATIVE)"}");
        Console.WriteLine();
        var catalog = Catalog.Load();
        foreach (var format in catalog.Formats)
            Console.WriteLine($"{format.Label,-12} 0x{format.Format:X4} {(format.Stable ? "stable" : "excluded")} {format.Tag}");
        Console.WriteLine();
        foreach (var route in catalog.Routes)
            Console.WriteLine($"route 0x{route.Source:X4} -> 0x{route.Target:X4}  {route.Method}, {route.Status.ToString().ToLowerInvariant()}  ({string.Join("/", route.SourceReleases)} -> {route.TargetRelease})");
        foreach (var route in catalog.Unsupported)
            Console.WriteLine($"no route 0x{route.Source:X4} -> 0x{route.Target:X4}: {route.Reason}");
        return 0;
    }

    private static int Inspect(string path)
    {
        var catalog = Catalog.Load();
        var report = SaveStateInspector.Inspect(path, catalog);
        Print(report, catalog);
        return report.IsValid ? 0 : 1;
    }

    private static int Scan(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var catalog = Catalog.Load();
        var latest = catalog.Formats.Where(x => x.Stable).MaxBy(x => x.Format)!;
        var failures = 0;
        foreach (var file in ConversionService.EnumerateStates(directory, recursive: true))
        {
            var report = SaveStateInspector.Inspect(file, catalog);
            var route = report.Format is { } format ? Describe(ConversionEngine.Assess(format, latest.Format, catalog), latest) : "";
            Console.WriteLine($"{(report.IsValid ? "OK" : "INVALID"),-7} {file}  {report.FormatText}  {route}");
            if (!report.IsValid) failures++;
        }
        return failures == 0 ? 0 : 1;
    }

    private static int Convert(string input, string target, string output, Options options)
    {
        var service = new ConversionService(Catalog.Load(), NativeConverter.Locate(options.Native));
        var result = service.Convert(input, target, output);
        if (result.SourceFormat is { } source)
            Console.WriteLine($"Source format: 0x{source:X4}");
        if (result.TargetFormat is { } targetFormat)
            Console.WriteLine($"Target stable release: {target} (0x{targetFormat:X4})");
        if (result.Route is { } route)
            Console.WriteLine($"Route: {route.Method}, {route.Status.ToString().ToLowerInvariant()} — {route.Evidence}");
        if (options.Verbose || !result.Succeeded)
            foreach (var line in result.Report) Console.WriteLine(line);
        Console.WriteLine($"Status: {result.Outcome.ToString().ToUpperInvariant()}: {result.Message}");
        if (result.Output is not null) Console.WriteLine($"Output: {result.Output}");
        return ExitCode(result.Outcome);
    }

    private static int ConvertInPlace(string input, string target, Options options)
    {
        var service = new ConversionService(Catalog.Load(), NativeConverter.Locate(options.Native));
        var source = Path.GetFullPath(input);
        var backups = BackupSet.Create(options.BackupRoot, Path.GetDirectoryName(source)!, target);
        var result = service.ConvertInPlace(source, target, backups);
        if (result.SourceFormat is { } sourceFormat)
            Console.WriteLine($"Source format: 0x{sourceFormat:X4}");
        if (options.Verbose || !result.Succeeded)
            foreach (var line in result.Report) Console.WriteLine(line);
        Console.WriteLine($"Status: {result.Outcome.ToString().ToUpperInvariant()}: {result.Message}");
        if (result.Backup is not null)
        {
            Console.WriteLine($"Original backed up to: {result.Backup}");
            Console.WriteLine($"Undo with: saveconvert restore \"{backups.Folder}\"");
        }
        return ExitCode(result.Outcome);
    }

    private static int ConvertDirectoryInPlace(string input, string target, Options options)
    {
        var service = new ConversionService(Catalog.Load(), NativeConverter.Locate(options.Native));
        var progress = new ConsoleProgress(r =>
            Console.WriteLine($"{r.Outcome.ToString().ToUpperInvariant(),-17} {Path.GetFileName(r.Source)}  {r.Message}"));
        var run = service.ConvertDirectoryInPlace(input, target, options.Recursive, options.BackupRoot, progress);
        var groups = run.Results.GroupBy(x => x.Outcome).Select(g => $"{g.Key}: {g.Count()}");
        Console.WriteLine($"Summary: {string.Join(", ", groups)} ({run.Results.Count} files)");
        if (run.BackupFolder is not null)
        {
            Console.WriteLine($"Originals backed up to: {run.BackupFolder}");
            Console.WriteLine($"Undo with: saveconvert restore \"{run.BackupFolder}\"");
        }
        return run.Results.Any(x => x.Outcome == ConversionOutcome.Failed) ? 1 : 0;
    }

    private static int ListBackups(Options options)
    {
        var sets = BackupSet.List(options.BackupRoot);
        if (sets.Count == 0)
            Console.WriteLine($"No backup sets in {options.BackupRoot}.");
        foreach (var set in sets)
        {
            Console.WriteLine(set.Folder);
            Console.WriteLine($"  {set.Description}");
        }
        return 0;
    }

    private static int Restore(string folder, string? output)
    {
        var set = BackupSet.Open(folder);
        var results = set.Restore(output);
        foreach (var result in results)
            Console.WriteLine($"{result.Outcome.ToString().ToUpperInvariant(),-15} {result.Destination ?? result.Original}  {result.Message}");
        var groups = results.GroupBy(x => x.Outcome).Select(g => $"{g.Key}: {g.Count()}");
        Console.WriteLine($"Summary: {string.Join(", ", groups)} ({results.Count} originals)");
        return results.All(x => x.Outcome is RestoreOutcome.Restored or RestoreOutcome.AlreadyOriginal) ? 0 : 1;
    }

    private static int ConvertDirectory(string input, string target, string output, Options options)
    {
        var service = new ConversionService(Catalog.Load(), NativeConverter.Locate(options.Native));
        var progress = new ConsoleProgress(r =>
            Console.WriteLine($"{r.Outcome.ToString().ToUpperInvariant(),-17} {Path.GetFileName(r.Source)}  {r.Message}"));
        var results = service.ConvertDirectory(input, target, output, options.Recursive, progress);
        var groups = results.GroupBy(x => x.Outcome).Select(g => $"{g.Key}: {g.Count()}");
        Console.WriteLine($"Summary: {string.Join(", ", groups)} ({results.Count} files)");
        return results.Any(x => x.Outcome == ConversionOutcome.Failed) ? 1 : 0;
    }

    private static int ExitCode(ConversionOutcome outcome) => outcome switch
    {
        ConversionOutcome.Converted or ConversionOutcome.AlreadyCompatible => 0,
        ConversionOutcome.Unsupported => 3,
        _ => 1,
    };

    private static string Describe(ConversionAssessment assessment, StableFormat target) => assessment.Status switch
    {
        ConversionStatus.Compatible => $"native {target.Label} format",
        ConversionStatus.Convertible => $"convertible to {target.Label} ({assessment.Route!.Status.ToString().ToLowerInvariant()})",
        _ => $"no route to {target.Label}",
    };

    private static void Print(Inspection r, Catalog catalog)
    {
        Console.WriteLine($"File: {r.Path}");
        if (r.Error is not null) { Console.WriteLine($"Inspection: FAILED ({r.Error})"); return; }
        var format = r.Format!.Value;
        Console.WriteLine($"Source Format: 0x{format:X4}");
        Console.WriteLine($"Stable Format Match: {r.KnownRelease ?? "Unknown stable format"} (format match alone does not establish compatibility)");
        Console.WriteLine($"Source Build Metadata: {r.BuildTag ?? "Unavailable"}");
        Console.WriteLine($"SHA-256: {r.Sha256}");
        Console.WriteLine($"Archive entries ({r.Entries.Length}):");
        foreach (var entry in r.Entries) Console.WriteLine($"  {entry}");
        if (r.MissingRequired.Length > 0) Console.WriteLine($"Missing required components: {string.Join(", ", r.MissingRequired)}");
        if (r.MissingOptional.Length > 0) Console.WriteLine($"Absent optional components: {string.Join(", ", r.MissingOptional)}");
        foreach (var target in catalog.Formats.Where(x => x.Stable && x.Format != format))
        {
            var assessment = ConversionEngine.Assess(format, target.Format, catalog);
            var text = assessment.Status == ConversionStatus.Convertible
                ? $"available ({assessment.Route!.Method}, {assessment.Route.Status.ToString().ToLowerInvariant()})"
                : $"UNSUPPORTED ({string.Join(" ", assessment.Blockers)})";
            Console.WriteLine($"Conversion to {target.Label}: {text}");
        }
    }

    /// <summary>Reports synchronously on the calling thread, keeping console output in order.</summary>
    private sealed class ConsoleProgress(Action<ConversionResult> handler) : IProgress<ConversionResult>
    {
        public void Report(ConversionResult value) => handler(value);
    }

    private sealed record Options(List<string> Positional, string? Target, string? Output, string? Native, bool Recursive, bool Verbose,
        bool InPlace, string? BackupDir)
    {
        /// <summary>Exactly one of --output and --in-place.</summary>
        public bool HasOneDestination => (Output is not null) != InPlace;

        public string BackupRoot => BackupDir ?? BackupSet.DefaultRoot;

        public static Options Parse(string[] args)
        {
            var positional = new List<string>();
            string? target = null, output = null, native = null, backupDir = null;
            bool recursive = false, verbose = false, inPlace = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--target" when i + 1 < args.Length: target = args[++i]; break;
                    case "--output" when i + 1 < args.Length: output = args[++i]; break;
                    case "--native" when i + 1 < args.Length: native = args[++i]; break;
                    case "--backup-dir" when i + 1 < args.Length: backupDir = args[++i]; break;
                    case "--in-place": inPlace = true; break;
                    case "--recursive": recursive = true; break;
                    case "--verbose": verbose = true; break;
                    default: positional.Add(args[i]); break;
                }
            }
            return new(positional, target, output, native, recursive, verbose, inPlace, backupDir);
        }
    }
}

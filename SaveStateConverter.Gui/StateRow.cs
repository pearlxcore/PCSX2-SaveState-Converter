using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SaveStateConverter.Gui;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
}

/// <summary>A PCSX2 version to convert for, as shown in the target list.</summary>
public sealed class TargetOption(StableFormat format, bool newest)
{
    public StableFormat Format { get; } = format;

    /// <summary>"PCSX2 2.8.0 – 2.8.2 (newest)"</summary>
    public string Display { get; } = $"PCSX2 {format.Label.Replace("-", " – ")}{(newest ? "   (newest)" : "")}";

    /// <summary>"PCSX2 2.8.x" or "PCSX2 2.4.0"</summary>
    public string ShortName { get; } = ShortNameOf(format);

    public static string ShortNameOf(StableFormat format) =>
        format.AllReleases.Count > 1 ? $"PCSX2 {string.Join('.', format.Version.Split('.').Take(2))}.x" : $"PCSX2 {format.Version}";

    public override string ToString() => Display;
}

public enum StateStatus
{
    Ready,
    ReadyUntested,
    AlreadyTarget,
    NotSupported,
    Damaged,
    Working,
    Converted,
    Failed,
    Skipped,
}

/// <summary>One save state in the list: who made it, and what can happen to it for the chosen version.</summary>
public sealed class StateRow : Observable
{
    private Inspection _inspection;
    private bool _selected;
    private StateStatus _status;
    private string _statusText = "";
    private string _statusDetail = "";
    private IReadOnlyList<string> _report = [];

    public StateRow(string path, Inspection inspection)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Name = SaveStateName.Parse(path);
        Title = GameTitles.Find(Name?.Serial) ?? Name?.Serial ?? FileName;
        Subtitle = Name is null ? FileName : $"{Name.Serial}  ·  {Name.SlotText}";
        _inspection = inspection;
    }

    public string Path { get; }
    public string FileName { get; }
    public SaveStateName? Name { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string ToolTipText => $"{Title}{Environment.NewLine}{FileName}";

    /// <summary>Disc serial ("Code" column).</summary>
    public string Serial => Name?.Serial ?? "";

    /// <summary>"Slot 1", "Resume", "Slot 10 (PCSX2 backup)".</summary>
    public string Slot => Name?.SlotText ?? "";

    private bool IsResume => Name is not null && Name.Slot.Equals("resume", StringComparison.OrdinalIgnoreCase);

    /// <summary>Icon (Segoe Fluent Icons / MDL2) for the kind of save state.</summary>
    public string KindGlyph => Name?.IsBackup == true ? "" : IsResume ? "" : "";

    public string KindText => Name?.IsBackup == true
        ? "PCSX2 backup of an overwritten slot"
        : IsResume ? "Resume state (saved when the game was closed)" : "Save-state slot";

    public Inspection Inspection
    {
        get => _inspection;
        set
        {
            if (Set(ref _inspection, value))
            {
                OnPropertyChanged(nameof(MadeWith));
                OnPropertyChanged(nameof(FormatText));
            }
        }
    }

    public string FormatText => Inspection.FormatText;

    /// <summary>"PCSX2 2.0.3", "PCSX2 2.5.258 (development build)".</summary>
    public string MadeWith
    {
        get
        {
            var build = Inspection.BuildTag;
            if (build is { Length: > 1 } && build.StartsWith('v') && char.IsDigit(build[1]))
            {
                var version = build[1..].Split(' ')[0];
                // "(migrated)" is the tag older builds wrote.
                if (build.EndsWith("(converted)", StringComparison.Ordinal) || build.EndsWith("(migrated)", StringComparison.Ordinal))
                    return $"Converted from PCSX2 {version}";
                var stable = Inspection.Format is { } format && Catalog.FindFormat(format) is { } release && release.Covers(version);
                return $"PCSX2 {version}{(stable ? "" : " (development build)")}";
            }
            return Inspection.KnownRelease is { } known ? $"PCSX2 {known}" : "Unknown PCSX2 version";
        }
    }

    public bool CanConvert => Status is StateStatus.Ready or StateStatus.ReadyUntested;

    public bool Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value && CanConvert))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public StateStatus Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
                OnPropertyChanged(nameof(CanConvert));
        }
    }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public IReadOnlyList<string> Report { get => _report; private set => Set(ref _report, value); }

    internal static Catalog Catalog { get; set; } = null!;

    /// <summary>Works out what can happen to this state when converting for <paramref name="target"/>.</summary>
    public void Evaluate(TargetOption target)
    {
        if (!Inspection.IsValid || Inspection.Format is not { } format)
        {
            Show(StateStatus.Damaged, "Can't be read",
                $"This file is damaged or is not a PCSX2 save state ({Inspection.Error ?? "missing parts: " + string.Join(", ", Inspection.MissingRequired)}).");
            return;
        }

        var assessment = ConversionEngine.Assess(format, target.Format.Format, Catalog);
        switch (assessment.Status)
        {
            case ConversionStatus.Compatible:
                Show(StateStatus.AlreadyTarget, $"Already works in {target.ShortName}",
                    $"This save state is already in the {target.ShortName} format. Nothing to do.");
                break;
            case ConversionStatus.Convertible when assessment.Route!.Status == RouteStatus.Validated:
                Show(StateStatus.Ready, "Ready to convert", "Tested conversion: " + assessment.Route.Evidence);
                break;
            case ConversionStatus.Convertible:
                Show(StateStatus.ReadyUntested, "Can convert (not yet tested)", assessment.Route!.Evidence);
                break;
            default:
                if (Catalog.FindFormat(format) is null)
                    Show(StateStatus.NotSupported, "PCSX2 version not supported",
                        $"Made by a PCSX2 version this tool doesn't support (save format 0x{format:X4}, build {Inspection.BuildTag ?? "unknown"}). " +
                        "Supported: official releases 2.0 to 2.8 and development builds with the same save format.");
                else if (format > target.Format.Format)
                    Show(StateStatus.NotSupported, "Made with a newer PCSX2",
                        $"This save state is from a newer PCSX2 than {target.ShortName}. Converting to an older version isn't supported. " +
                        string.Join(" ", assessment.Blockers));
                else
                    Show(StateStatus.NotSupported, "Can't convert to this version", string.Join(" ", assessment.Blockers));
                break;
        }
    }

    public void ShowWorking() => Show(StateStatus.Working, "Converting...", "");

    public void ShowResult(ConversionResult result, bool inPlace)
    {
        Report = result.Report;
        switch (result.Outcome)
        {
            case ConversionOutcome.Converted:
                Show(StateStatus.Converted, inPlace ? "Converted (backed up)" : "Converted", result.Message);
                break;
            case ConversionOutcome.AlreadyCompatible:
                Show(StateStatus.AlreadyTarget, "Already in this format", result.Message);
                break;
            case ConversionOutcome.Skipped:
                Show(StateStatus.Skipped, "Skipped: a converted file already exists", result.Message);
                break;
            default:
                Show(StateStatus.Failed, "Failed (see details)", result.Message);
                break;
        }
    }

    private void Show(StateStatus status, string text, string detail)
    {
        Status = status;
        StatusText = text;
        StatusDetail = detail;
        if (!CanConvert) Selected = false;
    }
}

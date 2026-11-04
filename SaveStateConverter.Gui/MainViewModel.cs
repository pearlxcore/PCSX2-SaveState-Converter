using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace SaveStateConverter.Gui;

public enum BannerKind
{
    None,
    Success,
    Warning,
}

/// <summary>An entry of the status filter next to the search box.</summary>
public sealed record FilterOption(string Name, Func<StateRow, bool> Matches)
{
    public override string ToString() => Name;
}

public sealed class MainViewModel : Observable
{
    private const string Title = "PCSX2 SaveState Converter";
    private readonly Catalog _catalog = Catalog.Load();
    private readonly NativeConverter? _native = NativeConverter.Locate();
    private CancellationTokenSource? _scan;
    private string _sourceFolder;
    private string _outputFolder;
    private string _backupFolder = BackupSet.DefaultRoot;
    private TargetOption _target;
    private bool _recursive;
    private bool _inPlace;
    private bool _busy;
    private int _progress;
    private int _progressMaximum = 1;
    private string _status = "";
    private string _summary = "";
    private string _bannerText = "";
    private BannerKind _banner;
    private string? _bannerFolder;
    private StateRow? _current;
    private BackupSet? _selectedBackup;
    private string _backupStatus = "";
    private string _search = "";
    private string _searchStatus = "";
    private FilterOption _statusFilter;

    public MainViewModel()
    {
        StateRow.Catalog = _catalog;
        StatusFilters =
        [
            new("All save states", _ => true),
            new("Ready to convert", row => row.CanConvert),
            new("Already in this format", row => row.Status == StateStatus.AlreadyTarget),
            new("Converted", row => row.Status == StateStatus.Converted),
            new("Can't be converted", row => row.Status is StateStatus.NotSupported or StateStatus.Damaged or StateStatus.Failed or StateStatus.Skipped),
        ];
        _statusFilter = StatusFilters[0];
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = item => item is StateRow row && StatusFilter.Matches(row) &&
                                  TextSearch.Matches(Search, row.Title, row.Subtitle, row.FileName);
        var formats = _catalog.Formats.Where(x => x.Stable).OrderByDescending(x => x.Format).ToArray();
        Targets = formats.Select((format, index) => new TargetOption(format, index == 0)).ToArray();
        _target = Targets[0];
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _sourceFolder = App.StartupFolder ?? Path.Combine(documents, "PCSX2", "sstates");
        _outputFolder = Path.Combine(documents, "PCSX2 SaveState Converter", $"converted-{_target.Format.Version}");

        ScanCommand = new RelayCommand(() => _ = ScanAsync(), () => !Busy);
        ConvertCommand = new RelayCommand(() => _ = ConvertAsync(), () => !Busy && _native is not null && ConvertCount > 0);
        SelectConvertibleCommand = new RelayCommand(() => SelectAll(true), () => !Busy && Rows.Count > 0);
        ClearSelectionCommand = new RelayCommand(() => SelectAll(false), () => !Busy && Rows.Count > 0);
        OpenResultCommand = new RelayCommand(() => OpenFolder(_bannerFolder), () => _bannerFolder is not null);
        DismissBannerCommand = new RelayCommand(() => ShowBanner(BannerKind.None, "", null));
        RestoreCommand = new RelayCommand(() => _ = RestoreAsync(null), () => !Busy && SelectedBackup is not null);
        OpenBackupCommand = new RelayCommand(() => OpenFolder(SelectedBackup?.Folder ?? BackupFolder), () => true);

        Status = _native is null ? "The converter component is missing; conversion is disabled." : "Ready.";
        RefreshBackups();
        if (Directory.Exists(_sourceFolder))
            _ = ScanAsync();
        else
            Summary = "Choose the folder that contains your save states.";
    }

    public IReadOnlyList<TargetOption> Targets { get; }
    public ObservableCollection<StateRow> Rows { get; } = [];

    /// <summary>The rows the list shows: all of them, or those matching <see cref="Search"/>.</summary>
    public ICollectionView RowsView { get; }

    /// <summary>Words to look for in the game title, disc serial, slot and file name.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? "")) return;
            RowsView.Refresh();
            OnPropertyChanged(nameof(SelectAllText));
            OnSelectionChanged(); // Convert works on the shown save states
        }
    }

    public IReadOnlyList<FilterOption> StatusFilters { get; }

    /// <summary>Which save states the list shows by status (next to the search box).</summary>
    public FilterOption StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (!Set(ref _statusFilter, value ?? StatusFilters[0])) return;
            RowsView.Refresh();
            OnPropertyChanged(nameof(SelectAllText));
            OnSelectionChanged();
        }
    }

    /// <summary>"Showing 6 of 97. 90 selected save states are hidden and won't be converted.", or empty without a search or filter.</summary>
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }

    private bool Filtering => !string.IsNullOrWhiteSpace(Search) || StatusFilter != StatusFilters[0];

    public string SelectAllText => Filtering ? "Select All Shown" : "Select All";

    private bool Shown(StateRow row) => RowsView.Filter?.Invoke(row) ?? true;

    private void UpdateSearchStatus()
    {
        if (!Filtering || Rows.Count == 0)
        {
            SearchStatus = "";
            return;
        }
        var shown = Rows.Count(Shown);
        var hiddenSelected = Rows.Count(r => r.Selected && !Shown(r));
        SearchStatus = (shown == 0 ? "No save states match." : $"Showing {shown} of {Rows.Count}.") +
                       (hiddenSelected == 0 ? "" : $"  {hiddenSelected} selected {(hiddenSelected == 1 ? "save state is" : "save states are")} hidden and won't be converted.");
    }
    public ObservableCollection<BackupSet> BackupSets { get; } = [];

    public ICommand ScanCommand { get; }
    public ICommand ConvertCommand { get; }
    public ICommand SelectConvertibleCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand OpenResultCommand { get; }
    public ICommand DismissBannerCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand OpenBackupCommand { get; }

    public bool ConverterMissing => _native is null;

    public string ConverterMissingText =>
        "This copy of the app has no converter next to it (it looks for native\\pcsx2-stateconvert.exe in " +
        $"{AppContext.BaseDirectory}), so converting is turned off. Use a release download: the single .exe, or " +
        "\"PCSX2 SaveState Converter.exe\" in the extracted zip folder, which includes the native folder.";

    public string SourceFolder
    {
        get => _sourceFolder;
        set
        {
            if (Set(ref _sourceFolder, value))
                _ = ScanAsync();
        }
    }

    public bool Recursive
    {
        get => _recursive;
        set
        {
            if (Set(ref _recursive, value))
                _ = ScanAsync();
        }
    }

    public TargetOption Target
    {
        get => _target;
        set
        {
            var previous = _target;
            if (!Set(ref _target, value)) return;
            // Keep the default output folder in step with the target unless the user changed it.
            if (Path.GetFileName(OutputFolder) == $"converted-{previous.Format.Version}")
                OutputFolder = Path.Combine(Path.GetDirectoryName(OutputFolder)!, $"converted-{value.Format.Version}");
            foreach (var row in Rows)
                row.Evaluate(value);
            SelectAll(true);
            UpdateSummary();
        }
    }

    public string OutputFolder { get => _outputFolder; set => Set(ref _outputFolder, value); }

    /// <summary>Replace the originals in the save-state folder (each is copied to a backup set first).</summary>
    public bool InPlace
    {
        get => _inPlace;
        set
        {
            if (!Set(ref _inPlace, value)) return;
            OnPropertyChanged(nameof(SaveCopies));
            OnPropertyChanged(nameof(ConvertText));
        }
    }

    public bool SaveCopies
    {
        get => !InPlace;
        set => InPlace = !value;
    }

    public string BackupFolder
    {
        get => _backupFolder;
        set
        {
            if (Set(ref _backupFolder, value))
                RefreshBackups();
        }
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            OnPropertyChanged(nameof(CanRestore));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    public int ProgressMaximum
    {
        get => _progressMaximum;
        private set
        {
            if (Set(ref _progressMaximum, value))
                OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>"16%" on the status-bar progress meter.</summary>
    public string ProgressText => $"{Progress * 100 / Math.Max(1, ProgressMaximum)}%";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string BannerText { get => _bannerText; private set => Set(ref _bannerText, value); }
    public BannerKind Banner { get => _banner; private set => Set(ref _banner, value); }
    public string OpenResultText => InPlace ? "Open backup folder" : "Open folder";
    public StateRow? Current { get => _current; set => Set(ref _current, value); }
    public string BackupStatus { get => _backupStatus; private set => Set(ref _backupStatus, value); }

    /// <summary>
    /// What Convert works on: the selected save states that are shown. Selected ones hidden by the
    /// search or status filter are left out; they stay selected and count again once shown.
    /// </summary>
    private StateRow[] ToConvert() => Rows.Where(r => r.Selected && Shown(r)).ToArray();

    public int ConvertCount => Rows.Count(r => r.Selected && Shown(r));

    public string ConvertText
    {
        get
        {
            var count = ConvertCount;
            var states = count == 1 ? "1 save state" : $"{count} save states";
            return count == 0 ? "Select save states to convert" : InPlace ? $"Convert {states} in place" : $"Convert {states}";
        }
    }

    public BackupSet? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (!Set(ref _selectedBackup, value)) return;
            OnPropertyChanged(nameof(CanRestore));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanRestore => SelectedBackup is not null && !Busy;

    /// <summary>Select every convertible state that is shown; "none" clears the whole selection.</summary>
    private void SelectAll(bool selected)
    {
        foreach (var row in Rows)
        {
            if (!selected) row.Selected = false;
            else if (Shown(row)) row.Selected = row.CanConvert;
        }
        OnSelectionChanged();
    }

    public void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(ConvertCount));
        OnPropertyChanged(nameof(ConvertText));
        UpdateSearchStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateSummary()
    {
        RowsView.Refresh(); // statuses changed: re-apply the status filter
        if (Rows.Count == 0)
        {
            Summary = Directory.Exists(SourceFolder) ? "No save states in this folder." : "Folder not found.";
            return;
        }
        var parts = new List<string> { Rows.Count == 1 ? "1 save state" : $"{Rows.Count} save states" };
        void Add(int count, string text) { if (count > 0) parts.Add($"{count} {text}"); }
        Add(Rows.Count(r => r.Status == StateStatus.Converted), "converted");
        Add(Rows.Count(r => r.CanConvert), "can be converted");
        var already = Rows.Count(r => r.Status == StateStatus.AlreadyTarget);
        Add(already, $"already {(already == 1 ? "works" : "work")} in {Target.ShortName}");
        Add(Rows.Count(r => r.Status is StateStatus.NotSupported or StateStatus.Damaged), "can't be converted");
        Summary = string.Join("  ·  ", parts);
    }

    private void ShowBanner(BannerKind kind, string text, string? folder)
    {
        Banner = kind;
        BannerText = text;
        _bannerFolder = folder;
        OnPropertyChanged(nameof(OpenResultText));
        CommandManager.InvalidateRequerySuggested();
    }

    public async Task ScanAsync()
    {
        _scan?.Cancel();
        var cancellation = new CancellationTokenSource();
        _scan = cancellation;
        var folder = SourceFolder;
        var recursive = Recursive;
        Rows.Clear();
        Current = null;
        ShowBanner(BannerKind.None, "", null);
        OnSelectionChanged();
        if (!Directory.Exists(folder))
        {
            Busy = false; // a scan this one replaced may still have been running
            Progress = 0;
            UpdateSummary();
            return;
        }

        Busy = true;
        try
        {
            Status = "Looking for save states...";
            var files = await Task.Run(() => ConversionService.EnumerateStates(folder, recursive), cancellation.Token);
            Progress = 0;
            ProgressMaximum = Math.Max(1, files.Count);
            var inspections = new Inspection[files.Count];
            var done = 0;
            var finished = false; // progress updates still queued after the scan must not overwrite the final status
            await Parallel.ForAsync(0, files.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2), CancellationToken = cancellation.Token },
                (index, _) =>
                {
                    inspections[index] = SaveStateInspector.Inspect(files[index], _catalog);
                    var count = Interlocked.Increment(ref done);
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (_scan == cancellation && !finished && count > Progress)
                        {
                            Progress = count;
                            Status = $"Checking save states... {count} of {files.Count}";
                        }
                    });
                    return ValueTask.CompletedTask;
                });
            finished = true;
            if (cancellation.IsCancellationRequested) return;
            for (var index = 0; index < files.Count; index++)
            {
                var row = new StateRow(files[index], inspections[index]);
                row.Evaluate(Target);
                row.Selected = row.CanConvert;
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(StateRow.Selected)) OnSelectionChanged(); };
                Rows.Add(row);
            }
            UpdateSummary();
            OnSelectionChanged();
            Status = "Ready.";
        }
        catch (OperationCanceledException)
        {
            // A newer scan replaced this one.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not read the folder: {ex.Message}";
        }
        finally
        {
            if (_scan == cancellation)
            {
                Busy = false;
                Progress = 0;
            }
        }
    }

    private async Task ConvertAsync()
    {
        if (_native is null) return;
        var service = new ConversionService(_catalog, _native);
        var selected = ToConvert();
        var target = Target;
        var root = SourceFolder;
        var output = OutputFolder;
        var inPlace = InPlace;
        BackupSet? backups = null;
        if (inPlace)
        {
            try
            {
                backups = BackupSet.Create(BackupFolder, root, target.Format.Version);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var states = selected.Length == 1 ? "1 save state" : $"{selected.Length} save states";
            var answer = MessageBox.Show(
                $"Replace {states} in\n{root}\nwith {target.ShortName} versions?\n\n" +
                $"Each original is first copied to\n{BackupFolder}\nand checked. You can put the originals back any time under Backups.\n\n" +
                "Close PCSX2 before you continue.",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        Busy = true;
        ShowBanner(BannerKind.None, "", null);
        Progress = 0;
        ProgressMaximum = Math.Max(1, selected.Length);
        int converted = 0, failed = 0;
        try
        {
            foreach (var row in selected)
            {
                Status = $"Converting {row.Title} ({Progress + 1} of {selected.Length})...";
                row.ShowWorking();
                var destination = Path.Combine(output, Path.GetRelativePath(root, row.Path));
                var result = await Task.Run(() => inPlace ? service.ConvertInPlace(row.Path, target.Format.Version, backups!) : service.Convert(row.Path, target.Format.Version, destination));
                if (inPlace && result.Outcome == ConversionOutcome.Converted)
                    row.Inspection = await Task.Run(() => SaveStateInspector.Inspect(row.Path, _catalog));
                row.ShowResult(result, inPlace);
                if (result.Outcome == ConversionOutcome.Converted) converted++;
                else if (result.Outcome is ConversionOutcome.Failed or ConversionOutcome.Skipped) failed++;
                Progress++;
            }

            var done = converted == 1 ? "1 save state" : $"{converted} save states";
            var problems = failed == 0 ? "" : $"  {failed} could not be converted: select them in the list to see why.";
            if (inPlace)
                ShowBanner(failed == 0 ? BannerKind.Success : BannerKind.Warning,
                    converted == 0
                        ? $"Nothing was replaced.{problems}"
                        : $"Done: {done} replaced with {target.ShortName} versions. The originals are kept in the backup folder (see Backups).{problems}",
                    backups!.Folder);
            else
                ShowBanner(failed == 0 ? BannerKind.Success : BannerKind.Warning,
                    converted == 0
                        ? $"Nothing was converted.{problems}"
                        : $"Done: {done} converted into {output}. Next, copy them into the sstates folder of {target.ShortName}.{problems}",
                    converted > 0 ? output : null);
            Status = "Ready.";
        }
        catch (Exception ex)
        {
            Status = $"Conversion stopped: {ex.Message}";
            MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Busy = false;
            Progress = 0;
            OnSelectionChanged();
            UpdateSummary();
            if (inPlace) RefreshBackups();
        }
    }

    /// <summary>Restores the selected backup set in place, or copies its originals to <paramref name="outputFolder"/>.</summary>
    public async Task RestoreAsync(string? outputFolder)
    {
        var set = SelectedBackup;
        if (set is null) return;
        var count = set.Manifest.Entries.Count == 1 ? "the 1 original save state" : $"the {set.Manifest.Entries.Count} original save states";
        var question = outputFolder is null
            ? $"Put back {count} from {set.Created:g} in\n{set.Manifest.SourceFolder}?\n\n" +
              "A file is replaced only if it is still the converted version. Save states you made after converting are left alone.\n\n" +
              "Close PCSX2 before you continue."
            : $"Copy {count} from {set.Created:g} into\n{outputFolder}?\n\nNothing in that folder is overwritten.";
        if (MessageBox.Show(question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        Busy = true;
        try
        {
            BackupStatus = "Restoring...";
            var results = await Task.Run(() => set.Restore(outputFolder));
            var restored = results.Count(r => r.Outcome is RestoreOutcome.Restored or RestoreOutcome.AlreadyOriginal);
            var problems = results.Where(r => r.Outcome is RestoreOutcome.Skipped or RestoreOutcome.Failed).ToArray();
            BackupStatus = problems.Length == 0
                ? $"Done: {restored} of {results.Count} original save states {(outputFolder is null ? "are back in place" : "copied")}."
                : $"{restored} of {results.Count} restored; {problems.Length} left as they are (see the message).";
            if (problems.Length > 0)
                MessageBox.Show(
                    "These were left as they are:\n\n" +
                    string.Join("\n", problems.Take(12).Select(r => $"{Path.GetFileName(r.Original)}: {r.Message}")) +
                    (problems.Length > 12 ? $"\n... and {problems.Length - 12} more" : ""),
                    Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            BackupStatus = $"Restore stopped: {ex.Message}";
            MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Busy = false;
        }
        if (outputFolder is null && string.Equals(Path.GetFullPath(set.Manifest.SourceFolder).TrimEnd('\\'), Path.GetFullPath(SourceFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            await ScanAsync();
    }

    private void RefreshBackups()
    {
        var previous = SelectedBackup?.Folder;
        BackupSets.Clear();
        try
        {
            foreach (var set in BackupSet.List(BackupFolder))
                BackupSets.Add(set);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable or invalid backup folder lists no sets.
        }
        SelectedBackup = BackupSets.FirstOrDefault(x => x.Folder == previous) ?? BackupSets.FirstOrDefault();
        BackupStatus = BackupSets.Count == 0 ? "No backups yet. They appear here after you replace save states in place." : "";
    }

    private static void OpenFolder(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
    }

    /// <summary>Opens Explorer on the file's folder with the file selected.</summary>
    public static void ShowInFolder(string file)
    {
        if (!File.Exists(file)) return;
        Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{file}\"", UseShellExecute = false });
    }
}

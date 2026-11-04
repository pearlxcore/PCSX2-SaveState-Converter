using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace SaveStateConverter.Gui;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        try
        {
            DataContext = _model = new MainViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The catalog could not be loaded: {ex.Message}", "PCSX2 SaveState Converter",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void BrowseSource(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder that contains your PCSX2 save states (sstates)", InitialDirectory = _model.SourceFolder };
        if (dialog.ShowDialog(this) == true)
            _model.SourceFolder = dialog.FolderName;
    }

    private void BrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder for the converted copies" };
        if (dialog.ShowDialog(this) == true)
            _model.OutputFolder = dialog.FolderName;
    }

    private void BrowseBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder for backups of replaced save states", InitialDirectory = _model.BackupFolder };
        if (dialog.ShowDialog(this) == true)
            _model.BackupFolder = dialog.FolderName;
    }

    private void ShowBackups(object sender, RoutedEventArgs e) => new BackupsWindow(_model) { Owner = this }.ShowDialog();

    private void ShowDetails(object sender, RoutedEventArgs e)
    {
        if (_model.Current is { } row)
            new DetailsWindow(row) { Owner = this }.ShowDialog();
    }

    private void ShowInFolder(object sender, RoutedEventArgs e)
    {
        if (_model.Current is { } row)
            MainViewModel.ShowInFolder(row.Path);
    }

    private void ShowAbout(object sender, RoutedEventArgs e)
    {
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "";
        MessageBox.Show(this,
            $"PCSX2 SaveState Converter {version}\n\n" +
            "Converts PCSX2 save states from older official releases (2.0 to 2.6) for newer ones, " +
            "keeping your originals or a verified backup of them.\n\n" +
            "Licensed under the GNU GPL v3 or later. PCSX2 is a trademark of its developers; " +
            "this tool is not affiliated with or endorsed by the PCSX2 team.",
            "About PCSX2 SaveState Converter", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExitClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Double-clicking a row (not the header or a check box) opens its details.</summary>
    private void RowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is CheckBox or DataGridColumnHeader) return;
            if (element is DataGridRow)
            {
                ShowDetails(sender, e);
                return;
            }
            if (element is not Visual) return;
        }
    }

    /// <summary>Enter opens the selected row's details; Space ticks or unticks it.</summary>
    private void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (_model.Current is not { } row) return;
        if (e.Key == Key.Enter)
        {
            ShowDetails(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && row.CanConvert)
        {
            row.Selected = !row.Selected;
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+F jumps to the search box, Ctrl+O opens a folder.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            BrowseSource(this, e);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Esc clears the search.</summary>
    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _model.Search.Length > 0)
        {
            _model.Search = "";
            e.Handled = true;
        }
    }

    /// <summary>Apply a typed folder with Enter instead of waiting for the box to lose focus.</summary>
    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}

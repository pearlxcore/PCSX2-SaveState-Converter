using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace SaveStateConverter.Gui;

/// <summary>Backup sets of replaced save states: put the originals back or copy them elsewhere.</summary>
public partial class BackupsWindow : Window
{
    private readonly MainViewModel _model;

    public BackupsWindow(MainViewModel model)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        DataContext = _model = model;
    }

    private void BrowseBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder for backups of replaced save states", InitialDirectory = _model.BackupFolder };
        if (dialog.ShowDialog(this) == true)
            _model.BackupFolder = dialog.FolderName;
    }

    private void RestoreToFolder(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedBackup is null || _model.Busy) return;
        var dialog = new OpenFolderDialog { Title = "Folder to copy the original save states into" };
        if (dialog.ShowDialog(this) == true)
            _ = _model.RestoreAsync(dialog.FolderName);
    }

    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

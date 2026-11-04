using System.Windows;

namespace SaveStateConverter.Gui;

/// <summary>Everything known about one save state, including the conversion report.</summary>
public partial class DetailsWindow : Window
{
    public DetailsWindow(StateRow row)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        DataContext = row;
    }

    private void ShowInFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is StateRow row)
            MainViewModel.ShowInFolder(row.Path);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

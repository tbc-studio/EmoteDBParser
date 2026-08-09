using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EmoteDBParser.App.ViewModels;

namespace EmoteDBParser.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        Closing += (_, _) => (DataContext as MainWindowViewModel)?.CleanupWorkspace();
        
        Loaded += async (_, _) => 
        {
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.AutoLoadEmotesIfConfiguredAsync();
            }
        };
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainVm)
            return;

        var settingsVm = new SettingsViewModel(mainVm);
        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsVm
        };

        await settingsWindow.ShowDialog(this);
    }
}

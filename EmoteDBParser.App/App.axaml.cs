using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EmoteDBParser.App.ViewModels;
using EmoteDBParser.App.Views;

namespace EmoteDBParser.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = new MainWindowViewModel();

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += (_, _) => mainViewModel.CleanupWorkspace();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => mainViewModel.CleanupWorkspace();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => mainViewModel.CleanupWorkspace();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

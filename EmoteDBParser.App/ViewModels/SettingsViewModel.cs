using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmoteDBParser.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainViewModel;

    [ObservableProperty]
    private string _pakFolderPath = "";

    [ObservableProperty]
    private bool _useSystemTemp = true;

    [ObservableProperty]
    private string _customOutputFolder = "";

    [ObservableProperty]
    private bool _autoLoadEnabled = true;
    
    [ObservableProperty]
    private bool _emoteDbFromServer;

    [ObservableProperty]
    private string _folderStatusText = "";

    [ObservableProperty]
    private string _saveStatusText = "";

    public SettingsViewModel() : this(null!)
    {
    }

    public SettingsViewModel(MainWindowViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        
        if (mainViewModel != null)
        {
            PakFolderPath = mainViewModel.PakFolderPath;
            UseSystemTemp = mainViewModel.UseSystemTemp;
            CustomOutputFolder = mainViewModel.CustomOutputFolder;
            AutoLoadEnabled = mainViewModel.AutoLoadEnabled;
        }

        UpdateFolderStatus();
    }

    partial void OnPakFolderPathChanged(string value)
    {
        UpdateFolderStatus();
    }

    private void UpdateFolderStatus()
    {
        if (string.IsNullOrWhiteSpace(PakFolderPath))
        {
            FolderStatusText = "No folder selected";
        }
        else if (Directory.Exists(PakFolderPath))
        {
            var pakFiles = Directory.GetFiles(PakFolderPath, "*.pak");
            FolderStatusText = pakFiles.Length > 0
                ? $"✓ Valid folder ({pakFiles.Length} .pak files found)"
                : "⚠ Folder exists but no .pak files found";
        }
        else
        {
            FolderStatusText = "✗ Folder does not exist";
        }
    }

    [RelayCommand]
    private async Task BrowsePakFolderAsync()
    {
        var folder = await BrowseFolderAsync("Select Pak Folder");
        if (folder != null)
        {
            PakFolderPath = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        var folder = await BrowseFolderAsync("Select Output Folder");
        if (folder != null)
        {
            CustomOutputFolder = folder;
        }
    }

    private async Task<string?> BrowseFolderAsync(string title)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is Window mainWindow)
            {
                var dialog = new Avalonia.Controls.OpenFolderDialog
                {
                    Title = title
                };

                var result = await dialog.ShowAsync(mainWindow);
                return result;
            }
        }

        return null;
    }

    public void SaveConfiguration()
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.PakFolderPath = PakFolderPath;
            _mainViewModel.UseSystemTemp = UseSystemTemp;
            _mainViewModel.CustomOutputFolder = CustomOutputFolder;
            _mainViewModel.AutoLoadEnabled = AutoLoadEnabled;
            _mainViewModel.AutoLoadEnabled = AutoLoadEnabled;
            _mainViewModel.SaveConfiguration();

            SaveStatusText = "Settings saved successfully!";
        }
    }
}

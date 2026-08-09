using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CUE4Parse.UE4.Versions;
using Microsoft.Win32;
using Serilog;
namespace EmoteDBParser.App.ViewModels;
public partial class SettingsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainViewModel;
    [ObservableProperty]
    private string _pakFolderPath = "";
    [ObservableProperty]
    private bool _useSystemTemp = true;
    [ObservableProperty]
    private string _customOutputFolder = GetSteamGame(578080, @"\TslGame\Content\Paks", EGame.GAME_PlayerUnknownsBattlegrounds); 
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
    private void FindGameAutomatically()
    {
        FolderStatusText = "Searching for game installation…";
        var detected = TryFindPubgPakFolder();
        if (!string.IsNullOrWhiteSpace(detected) && Directory.Exists(detected))
        {
            PakFolderPath = detected;
            UpdateFolderStatus(); 
        }
        else
        {
            FolderStatusText = "✗ Could not find the game installation automatically. Please browse for the pak folder manually.";
        }
    }
    public static string? TryFindPubgPakFolder()
    {
        var path = GetSteamGame(578080, @"\TslGame\Content\Paks", EGame.GAME_PlayerUnknownsBattlegrounds);
        return string.IsNullOrWhiteSpace(path) ? null : path;
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
    private static string GetSteamGame(int id, string pakDirectory, EGame ueVersion, string aesKey = "")
    {
        var steamInfo = SteamDetection.GetSteamGameById(id);
        if (steamInfo is not null)
        {
            Console.WriteLine($"Found {steamInfo.Name} in Steam manifests");
            return Path.Combine(steamInfo.GameRoot, pakDirectory.TrimStart('\\'));
        }
        Console.WriteLine($"Could not find Steam game with AppID {id}");
        return "";
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
    public static class SteamDetection
    {
        private static readonly List<AppInfo> _steamApps;
        static SteamDetection()
        {
            _steamApps = GetSteamApps(GetSteamLibs());
        }
        public static AppInfo GetSteamGameById(int id) => _steamApps.FirstOrDefault(app => app.Id == id.ToString());
        private static List<AppInfo> GetSteamApps(IEnumerable<string> steamLibs)
        {
            var apps = new List<AppInfo>();
            foreach (var files in steamLibs
                         .Select(lib => Path.Combine(lib, "SteamApps"))
                         .Select(appMetaDataPath => Directory.Exists(appMetaDataPath) ? Directory.GetFiles(appMetaDataPath, "*.acf") : null)
                         .Where(files => files != null))
            {
                apps.AddRange(files.Select(GetAppInfo).Where(appInfo => appInfo != null));
            }
            return apps;
        }
        private static AppInfo GetAppInfo(string appMetaFile)
        {
            var fileDataLines = File.ReadAllLines(appMetaFile);
            var dic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in fileDataLines)
            {
                var match = Regex.Match(line, @"\s*""(?<key>\w+)""\s+""(?<val>.*)""");
                if (!match.Success) continue;
                var key = match.Groups["key"].Value;
                var val = match.Groups["val"].Value;
                dic[key] = val;
            }
            if (!dic.TryGetValue("appid", out var appId) ||
                !dic.TryGetValue("name", out var name) ||
                !dic.TryGetValue("installDir", out var installDir)) return null;
            var path = Path.GetDirectoryName(appMetaFile) ?? "";
            var libGameRoot = Path.Combine(path, "common", installDir);
            return Directory.Exists(libGameRoot) ? new AppInfo { Id = appId, Name = name, GameRoot = libGameRoot } : null;
        }
        private static List<string> GetSteamLibs()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null || !Directory.Exists(steamPath)) return new List<string>();
            var libraries = new List<string> { steamPath };
            var listFile = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(listFile)) return new List<string>();
            var lines = File.ReadAllLines(listFile);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"""(?<path>\w:\\\\.*)""");
                if (!match.Success) continue;
                var path = match.Groups["path"].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path) && !libraries.Contains(path))
                {
                    libraries.Add(path);
                }
            }
            return libraries;
        }
        private static string GetSteamPath() => (string) Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", ""); 
        public class AppInfo
        {
            public string Id { get; internal set; }
            public string Name { get; internal set; }
            public string GameRoot { get; internal set; }
            public override string ToString()
            {
                return $"{Name} ({Id})";
            }
        }
    }
}
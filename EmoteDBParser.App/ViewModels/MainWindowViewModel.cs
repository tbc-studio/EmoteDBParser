using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmoteDBParser;
namespace EmoteDBParser.App.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    private readonly List<EmoteCardViewModel> _allEmotes = new();
    private string? _activeWorkspaceDirectory;
    private CancellationTokenSource? _loadCts;
    private const string ConfigFileName = "EmoteDBParser.config.json";
    [ObservableProperty]
    private string _pakFolderPath = "";
    [ObservableProperty]
    private bool _useSystemTemp = true;
    [ObservableProperty]
    private string _customOutputFolder = "";
    [ObservableProperty]
    private string _statusText = "Enter a folder containing .pak files, then click \"Load Emotes\".";
    [ObservableProperty]
    private string? _errorMessage;
    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private string _searchText = "";
    [ObservableProperty]
    private EmoteCardViewModel? _selectedEmote;
    [ObservableProperty]
    private bool _hasResults;
    [ObservableProperty]
    private bool _autoLoadEnabled;
    [ObservableProperty]
    private bool _emoteDbFromServer;
    public ObservableCollection<EmoteCardViewModel> Emotes { get; } = new();
    public MainWindowViewModel()
    {
        LoadConfiguration();
    }
    public bool IsDetailOpen => SelectedEmote != null;
    partial void OnSelectedEmoteChanged(EmoteCardViewModel? oldValue, EmoteCardViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
            newValue.IsSelected = true;
        OnPropertyChanged(nameof(IsDetailOpen));
    }
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnIsBusyChanged(bool value) => LoadEmotesCommand.NotifyCanExecuteChanged();
    [RelayCommand]
    private void SelectEmote(EmoteCardViewModel? emote)
    {
        SelectedEmote = ReferenceEquals(SelectedEmote, emote) ? null : emote;
    }
    [RelayCommand]
    private void CloseDetail() => SelectedEmote = null;
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadEmotesAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(PakFolderPath) || !Directory.Exists(PakFolderPath))
        {
            ErrorMessage = "Please choose a valid folder containing .pak files.";
            return;
        }
        CleanupWorkspace();
        string workspace;
        try
        {
            workspace = PrepareWorkspaceDirectory();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create the output folder: {ex.Message}";
            return;
        }
        _activeWorkspaceDirectory = workspace;
        IsBusy = true;
        SelectedEmote = null;
        Emotes.Clear();
        _allEmotes.Clear();
        HasResults = false;
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var progress = new Progress<string>(text => StatusText = text);
        try
        {
            string pakPath = PakFolderPath;
            var rows = await Task.Run(
                () => Initializer.ExportPubgAssets(pakPath, workspace, progress, token),
                token);
            foreach (var row in rows.OrderBy(r => r.Id))
                _allEmotes.Add(new EmoteCardViewModel(row));
            ApplyFilter();
            HasResults = _allEmotes.Count > 0;
            StatusText = HasResults
                ? $"Loaded {_allEmotes.Count} emotes."
                : "No emotes were found in this pak folder.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load emotes: {ex.Message}";
            StatusText = "Failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
    private bool CanLoad() => !IsBusy;
    private void ApplyFilter()
    {
        IEnumerable<EmoteCardViewModel> filtered = _allEmotes;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = _allEmotes.Where(e =>
                e.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Row.Row.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }
        Emotes.Clear();
        foreach (var e in filtered)
            Emotes.Add(e);
    }
    private string PrepareWorkspaceDirectory()
    {
        string baseDir = UseSystemTemp || string.IsNullOrWhiteSpace(CustomOutputFolder)
            ? Path.GetTempPath()
            : CustomOutputFolder;
        Directory.CreateDirectory(baseDir);
        string sessionDir = Path.Combine(baseDir, "EmoteDBParser_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        return sessionDir;
    }
    public void CleanupWorkspace()
    {
        _loadCts?.Cancel();
        var dir = _activeWorkspaceDirectory;
        _activeWorkspaceDirectory = null;
        if (dir == null || !Directory.Exists(dir))
            return;
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
        }
    }
    private void LoadConfiguration()
    {
        try
        {
            string configPath = GetConfigFilePath();
            if (!File.Exists(configPath))
            {
                TryAutoDetectPakFolderOnFirstRun();
                return;
            }
            string json = File.ReadAllText(configPath);
            var config = System.Text.Json.JsonSerializer.Deserialize<ConfigData>(json);
            if (config != null)
            {
                PakFolderPath = config.PakFolderPath ?? "";
                UseSystemTemp = config.UseSystemTemp;
                CustomOutputFolder = config.CustomOutputFolder ?? "";
                AutoLoadEnabled = config.AutoLoadEnabled;
            }
        }
        catch
        {
        }
    }
    private void TryAutoDetectPakFolderOnFirstRun()
    {
        try
        {
            var detected = SettingsViewModel.TryFindPubgPakFolder();
            if (!string.IsNullOrWhiteSpace(detected) && Directory.Exists(detected))
            {
                PakFolderPath = detected;
                StatusText = "Found your game installation automatically. Click \"Load Emotes\" to continue, " +
                             "or choose a different folder in Settings.";
            }
            else
            {
                StatusText = "Couldn't find your game installation automatically. " +
                              "Please select the pak folder in Settings.";
            }
        }
        catch
        {
        }
        SaveConfiguration();
    }
    public void SaveConfiguration()
    {
        try
        {
            var config = new ConfigData
            {
                PakFolderPath = PakFolderPath,
                UseSystemTemp = UseSystemTemp,
                CustomOutputFolder = CustomOutputFolder,
                AutoLoadEnabled = AutoLoadEnabled
            };
            string json = System.Text.Json.JsonSerializer.Serialize(config, 
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            string configPath = GetConfigFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, json);
        }
        catch
        {
        }
    }
    private static string GetConfigFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "EmoteDBParser", ConfigFileName);
    }
    public async Task AutoLoadEmotesIfConfiguredAsync()
    {
        if (!AutoLoadEnabled || string.IsNullOrWhiteSpace(PakFolderPath) || !Directory.Exists(PakFolderPath))
            return;
        await Task.Delay(500);
        await LoadEmotesAsync();
    }
    private sealed class ConfigData
    {
        [System.Text.Json.Serialization.JsonPropertyName("pakFolderPath")]
        public string? PakFolderPath { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("useSystemTemp")]
        public bool UseSystemTemp { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("customOutputFolder")]
        public string? CustomOutputFolder { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("autoLoadEnabled")]
        public bool AutoLoadEnabled { get; set; }
    }
}
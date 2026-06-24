using System;
using System.IO;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using Velopack;
using Velopack.Sources;

namespace AvaloniaTestApp.ViewModels;

public class AppSettingsViewModel : ReactiveObject
{
    // ── Paths ────────────────────────────────────────────────
    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AvaloniaTestApp", "app_settings.json");
    
    public static AppSettingsViewModel Current { get; private set; } = new();

    // ── Theme & Appearance ───────────────────────────────────
    private string _selectedTheme = "Dark";
    public string SelectedTheme
    {
        get => _selectedTheme;
        set => this.RaiseAndSetIfChanged(ref _selectedTheme, value);
    }

    private string _editorFont = "Cascadia Code";
    public string EditorFont
    {
        get => _editorFont;
        set => this.RaiseAndSetIfChanged(ref _editorFont, value);
    }

    private int _fontSize = 13;
    public int FontSize
    {
        get => _fontSize;
        set => this.RaiseAndSetIfChanged(ref _fontSize, value);
    }

    // ── User Preferences ────────────────────────────────────
    private bool _autoSaveEnabled = true;
    public bool AutoSaveEnabled
    {
        get => _autoSaveEnabled;
        set => this.RaiseAndSetIfChanged(ref _autoSaveEnabled, value);
    }

    private bool _confirmOnDelete = true;
    public bool ConfirmOnDelete
    {
        get => _confirmOnDelete;
        set => this.RaiseAndSetIfChanged(ref _confirmOnDelete, value);
    }

    private bool _showRowNumbers = true;
    public bool ShowRowNumbers
    {
        get => _showRowNumbers;
        set => this.RaiseAndSetIfChanged(ref _showRowNumbers, value);
    }

    private bool _wrapLongLines = false;
    public bool WrapLongLines
    {
        get => _wrapLongLines;
        set => this.RaiseAndSetIfChanged(ref _wrapLongLines, value);
    }

    private int _queryResultLimit = 1000;
    public int QueryResultLimit
    {
        get => _queryResultLimit;
        set => this.RaiseAndSetIfChanged(ref _queryResultLimit, value);
    }

    // ── Export & File Paths ──────────────────────────────────
    private string _exportFolderPath = "";
    public string ExportFolderPath
    {
        get => _exportFolderPath;
        set => this.RaiseAndSetIfChanged(ref _exportFolderPath, value);
    }

    private string _defaultExportFormat = "CSV";
    public string DefaultExportFormat
    {
        get => _defaultExportFormat;
        set => this.RaiseAndSetIfChanged(ref _defaultExportFormat, value);
    }

    private string _querySavePath = "";
    public string QuerySavePath
    {
        get => _querySavePath;
        set => this.RaiseAndSetIfChanged(ref _querySavePath, value);
    }

    // ── Language & Locale ────────────────────────────────────
    private string _selectedLanguage = "English";
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
    }

    private string _dateFormat = "YYYY-MM-DD (ISO)";
    public string DateFormat
    {
        get => _dateFormat;
        set => this.RaiseAndSetIfChanged(ref _dateFormat, value);
    }

    private string _decimalSeparator = "Period ( 1,000.00 )";
    public string DecimalSeparator
    {
        get => _decimalSeparator;
        set => this.RaiseAndSetIfChanged(ref _decimalSeparator, value);
    }

    // ── Updates ──────────────────────────────────────────────
    private bool _autoCheckUpdates = true;
    public bool AutoCheckUpdates
    {
        get => _autoCheckUpdates;
        set => this.RaiseAndSetIfChanged(ref _autoCheckUpdates, value);
    }

    private string _updateStatusMessage = "";
    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        set => this.RaiseAndSetIfChanged(ref _updateStatusMessage, value);
    }

    public string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "1.0.0";

    // ── Status (mirrors your DB tab pattern) ─────────────────
    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private Color _statusColor = Color.Parse("#5F5E5A");
    public Color StatusColor
    {
        get => _statusColor;
        set => this.RaiseAndSetIfChanged(ref _statusColor, value);
    }

    // ── Commands ─────────────────────────────────────────────
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ResetCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CheckForUpdatesCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseExportFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseQuerySavePathCommand { get; }

    public AppSettingsViewModel()
    {
        SaveCommand = ReactiveCommand.CreateFromTask(
            async () => await SaveAsync());  // ← remove outputScheduler

        ResetCommand = ReactiveCommand.Create(
            ResetToDefaults);

        CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(
            async () => await CheckForUpdatesAsync());  // ← remove outputScheduler

        BrowseExportFolderCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var folder = await PickFolderAsync();
            if (folder is not null) ExportFolderPath = folder;
        });  // ← remove outputScheduler

        BrowseQuerySavePathCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var folder = await PickFolderAsync();
            if (folder is not null) QuerySavePath = folder;
        });  // ← remove outputScheduler

        _ = LoadAsync();
    }

    // ── Save / Load ──────────────────────────────────────────
    public async Task SaveAsync()
    {
        bool success = false;
        string errorMessage = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                var folder = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var snapshot = ToSnapshot();
                string json = JsonSerializer.Serialize(snapshot,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
                success = true;
            }
            catch (Exception ex) { errorMessage = ex.Message; }
        });

        // Back on UI thread after await — same pattern as your SaveLocallyAsync
        if (success)
        {
            StatusText  = "Settings saved";
            StatusColor = Color.Parse("#1D9E75");
            ApplySettings();
        }
        else
        {
            StatusText  = $"Save failed: {errorMessage}";
            StatusColor = Color.Parse("#E24B4A");
        }
    }

    public async Task LoadAsync()
    {
        AppSettingsSnapshot? snapshot = null;

        await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;
                string json = File.ReadAllText(SettingsFilePath);
                snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(json);
            }
            catch { /* silent fail */ }
        });

        // Now back on UI thread — safe to update properties and apply theme
        if (snapshot is not null)
        {
            FromSnapshot(snapshot);
            ApplySettings();
        }
    }

    private void ResetToDefaults()
    {
        SelectedTheme       = "Dark";
        EditorFont          = "Cascadia Code";
        FontSize            = 13;
        AutoSaveEnabled     = true;
        ConfirmOnDelete     = true;
        ShowRowNumbers      = true;
        WrapLongLines       = false;
        QueryResultLimit    = 1000;
        ExportFolderPath    = "";
        DefaultExportFormat = "CSV";
        QuerySavePath       = "";
        SelectedLanguage    = "English";
        DateFormat          = "YYYY-MM-DD (ISO)";
        DecimalSeparator    = "Period ( 1,000.00 )";
        AutoCheckUpdates    = true;
        StatusText          = "Reset to defaults";
        StatusColor         = Color.Parse("#1D9E75");
        ApplySettings();
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusMessage = "Checking...";
        try
        {
            var mgr = new UpdateManager(new GithubSource(
                "https://github.com/SommeanDev/Avalonia_DBGEN", null, false));

            if (!mgr.IsInstalled)
            {
                UpdateStatusMessage = "Not installed via Velopack";
                return;
            }

            var update = await mgr.CheckForUpdatesAsync();
            if (update is null)
            {
                UpdateStatusMessage = "Already up to date";
                return;
            }

            UpdateStatusMessage = $"Downloading update {update.TargetFullRelease.Version}...";
            await mgr.DownloadUpdatesAsync(update);

            UpdateStatusMessage = "Update ready. Restarting...";
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Check failed: {ex.Message}";
        }
    }

    // ── Snapshot helpers (keeps JSON model separate from VM) ──
    private AppSettingsSnapshot ToSnapshot() => new()
    {
        SelectedTheme       = SelectedTheme,
        EditorFont          = EditorFont,
        FontSize            = FontSize,
        AutoSaveEnabled     = AutoSaveEnabled,
        ConfirmOnDelete     = ConfirmOnDelete,
        ShowRowNumbers      = ShowRowNumbers,
        WrapLongLines       = WrapLongLines,
        QueryResultLimit    = QueryResultLimit,
        ExportFolderPath    = ExportFolderPath,
        DefaultExportFormat = DefaultExportFormat,
        QuerySavePath       = QuerySavePath,
        SelectedLanguage    = SelectedLanguage,
        DateFormat          = DateFormat,
        DecimalSeparator    = DecimalSeparator,
        AutoCheckUpdates    = AutoCheckUpdates,
    };

    private void FromSnapshot(AppSettingsSnapshot s)
    {
        SelectedTheme       = s.SelectedTheme;
        EditorFont          = s.EditorFont;
        FontSize            = s.FontSize;
        AutoSaveEnabled     = s.AutoSaveEnabled;
        ConfirmOnDelete     = s.ConfirmOnDelete;
        ShowRowNumbers      = s.ShowRowNumbers;
        WrapLongLines       = s.WrapLongLines;
        QueryResultLimit    = s.QueryResultLimit;
        ExportFolderPath    = s.ExportFolderPath;
        DefaultExportFormat = s.DefaultExportFormat;
        QuerySavePath       = s.QuerySavePath;
        SelectedLanguage    = s.SelectedLanguage;
        DateFormat          = s.DateFormat;
        DecimalSeparator    = s.DecimalSeparator;
        AutoCheckUpdates    = s.AutoCheckUpdates;
    }
    
    private static async Task<string?> PickFolderAsync()
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var window = desktop?.MainWindow;
        if (window is null) return null;

        var result = await TopLevel.GetTopLevel(window)!
            .StorageProvider.OpenFolderPickerAsync(new() { Title = "Select Folder" });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }
    
    private void ApplySettings()
    {
        App.ApplyTheme(SelectedTheme);
        App.ApplyFontSettings(EditorFont, FontSize);
        // QueryResultLimit, DateFormat, etc. are read directly
        // by other ViewModels when they need them — see step 4
    }
}

// ── Separate POCO for JSON (never expose VM to serializer) ────
public class AppSettingsSnapshot
{
    public string SelectedTheme       { get; set; } = "Dark";
    public string EditorFont          { get; set; } = "Cascadia Code";
    public int    FontSize            { get; set; } = 13;
    public bool   AutoSaveEnabled     { get; set; } = true;
    public bool   ConfirmOnDelete     { get; set; } = true;
    public bool   ShowRowNumbers      { get; set; } = true;
    public bool   WrapLongLines       { get; set; } = false;
    public int    QueryResultLimit    { get; set; } = 1000;
    public string ExportFolderPath    { get; set; } = "";
    public string DefaultExportFormat { get; set; } = "CSV";
    public string QuerySavePath       { get; set; } = "";
    public string SelectedLanguage    { get; set; } = "English";
    public string DateFormat          { get; set; } = "YYYY-MM-DD (ISO)";
    public string DecimalSeparator    { get; set; } = "Period ( 1,000.00 )";
    public bool   AutoCheckUpdates    { get; set; } = true;
}

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using Avalonia.Media;
using AvaloniaTestApp.Models;
using ReactiveUI;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using AvaloniaTestApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;

namespace AvaloniaTestApp.ViewModels;

public partial class MainWindowViewModel : ReactiveObject
{
    private bool _isPaneOpen = false;
    private int _selectedTabIndex;
    public bool IsPaneOpen
    {
        get => _isPaneOpen;
        set => this.RaiseAndSetIfChanged(ref _isPaneOpen, value);
    }
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
    }
    
    // This method can be called directly by a Button's Command binding
    public void TogglePane()
    {
        IsPaneOpen = !IsPaneOpen;
    }
    
    public ViewBuilderViewModel ViewBuilder { get; } = new();
    public AppSettingsViewModel AppSettings { get; } = AppSettingsViewModel.Current;

    // =================== DB ==================
    
    // Properties
// --- Data Properties ---
    private string _host = "localhost";
    public string Host { get => _host; set => this.RaiseAndSetIfChanged(ref _host, value); }

    private string _port = "5432";
    public string Port { get => _port; set => this.RaiseAndSetIfChanged(ref _port, value); }

    private string _dbName = "";
    public string DatabaseName { get => _dbName; set => this.RaiseAndSetIfChanged(ref _dbName, value); }

    private string _username = "";
    public string Username { get => _username; set => this.RaiseAndSetIfChanged(ref _username, value); }

    private string _password = "";
    public string Password { get => _password; set => this.RaiseAndSetIfChanged(ref _password, value); }

    private bool _connectionSuccess = false;

    public bool ConnectionSuccess
    {
        get =>  _connectionSuccess;
        set => this.RaiseAndSetIfChanged(ref _connectionSuccess, value);
    }

    private ObservableCollection<string> _schemas = new ObservableCollection<string>();
    public ObservableCollection<string> Schemas { get => _schemas; set => this.RaiseAndSetIfChanged(ref _schemas, value); }
    
    private string _selectedSchema;
    public string SelectedSchema
    {
        get => _selectedSchema;
        set => this.RaiseAndSetIfChanged(ref _selectedSchema, value);
    }
    
    private ObservableCollection<DatabaseObject> _databaseObjects = new();
    public ObservableCollection<DatabaseObject> DatabaseObjects
    {
        get => _databaseObjects;
        set => this.RaiseAndSetIfChanged(ref _databaseObjects, value);
    }
    
    private string _contextCol = "";
    public string ContextCol
    {
        get => _contextCol;
        set => this.RaiseAndSetIfChanged(ref _contextCol, value);
    }
    
    // --- Status Properties ---
    private string _statusText = "Not connected";
    public string ConnectionStatusText { get => _statusText; set => this.RaiseAndSetIfChanged(ref _statusText, value); }

    private Color _statusColor = Color.Parse("#5F5E5A");
    public Color ConnectionStatusColor { get => _statusColor; set => this.RaiseAndSetIfChanged(ref _statusColor, value); }
    
    public ReactiveCommand<Unit, Unit> TestConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCredentialsCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadTablesCommand { get; }
    
    
    public MainWindowViewModel()
    {
        SaveCredentialsCommand = ReactiveCommand.CreateFromTask(
            async () => await SaveLocallyAsync(),
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance
        );

        TestConnectionCommand = ReactiveCommand.CreateFromTask(
            async () => await RunTestConnectionWorkflowAsync(),
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance
        );

        LoadTablesCommand = ReactiveCommand.CreateFromTask(
            async () => await RunLoadTablesWorkflowAsync(),
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance
        );
        
        
        // ======================= TAB 2 - Script Gen ===========================
        AddColumnCommand = ReactiveCommand.Create(() => Columns.Add(new ColumnViewModel()));

        RemoveColumnCommand = ReactiveCommand.Create<ColumnViewModel>(col => Columns.Remove(col));

        GenerateSqlCommand = ReactiveCommand.Create(GenerateSqlFromTemplate,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        CopySqlCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrEmpty(GeneratedSql)) return;

            var desktop = Avalonia.Application.Current?.ApplicationLifetime 
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
    
            var mainWindow = desktop?.MainWindow;
            if (mainWindow is null) return;

            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard;

            if (clipboard is not null)
            {
                // Fire-and-forget the async task directly on the UI main loop safely
                _ = clipboard.SetTextAsync(GeneratedSql);
            }
        }, outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance); // <-- CRITICAL
        
        ExecuteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // execute SQL against DB later
            await Task.CompletedTask;
        });
        ShowGeneratedSqlCommand = ReactiveCommand.Create(() => { ShowGeneratedSql = true; });
        ShowTemplateEditorCommand = ReactiveCommand.Create(() => { ShowGeneratedSql = false; });
        SaveTemplateCommand = ReactiveCommand.Create(SaveSelectedTemplate);
        ReloadTemplateCommand = ReactiveCommand.Create(LoadSelectedTemplate);

        LoadTemplates();
        LoadSelectedTemplate();

        _ = LoadCredentialsAsync();
    }

    private async Task LoadCredentialsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                string filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AvaloniaTestApp", "db_config.json");

                if (!File.Exists(filePath)) return;

                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<DatabaseConfig>(json);
                if (config is null) return;

                Host         = config.Host;
                Port         = config.Port;
                DatabaseName = config.DatabaseName;
                Username     = config.Username;
                Password     = config.Password;
            }
            catch { /* silent fail — first run or corrupted config */ }
        });
    }
    
    private async Task SaveLocallyAsync()
    {
        bool success = false;
        string errorMessage = string.Empty;

        // 1. Heavy Work on background thread
        await Task.Run(() =>
        {
            try
            {
                var config = new DatabaseConfig
                {
                    Host = Host,
                    Port = Port,
                    DatabaseName = DatabaseName,
                    Username = Username,
                    Password = Password,
                };

                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvaloniaTestApp");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string filePath = Path.Combine(folder, "db_config.json");
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                
                success = true;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
        });

        // 2. UI Updates (Now back on the Main Thread automatically because we 'awaited' the Task)
        if (success)
        {
            ConnectionStatusText = "Settings saved locally";
            ConnectionStatusColor = Color.Parse("#1D9E75");
        }
        else
        {
            ConnectionStatusText = $"Save failed: {errorMessage}";
            ConnectionStatusColor = Color.Parse("#E24B4A");
        }
    }

    private async Task RunTestConnectionWorkflowAsync()
{
    ConnectionStatusText = "Testing connection...";
    ConnectionStatusColor = Color.Parse("#EF9F27"); // Amber

    var currentConfig = new DatabaseConfig
    {
        Host = Host, Port = Port, DatabaseName = DatabaseName, Username = Username, Password = Password
    };

    var dbService = new DatabaseRepository(currentConfig);

    try
    {
        // Force a strict 6-second absolute timeout limit at the app level
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));

        // Create the connection task
        var connectionTask = dbService.TestConnectionAsync();

        // Wait for EITHER the connection task to complete, OR the timeout token to expire
        var completedTask = await Task.WhenAny(connectionTask, Task.Delay(-1, cts.Token));

        if (completedTask == connectionTask)
        {
            // The connection task finished! Grab the result
            var (success, message) = await connectionTask;

            if (success)
            {
                ConnectionStatusText = $"Connected to {DatabaseName} @ {Host}";
                ConnectionStatusColor = Color.Parse("#1D9E75"); // Teal
                ConnectionSuccess = true;

                // Load schemas...
                var fetchedSchemas = await dbService.GetSchemaAsync();
                Schemas.Clear();
                foreach (var schema in fetchedSchemas) Schemas.Add(schema);
                if (Schemas.Contains("public")) SelectedSchema = "public";
            }
            else
            {
                ConnectionStatusText = $"Connection failed: {message}";
                ConnectionStatusColor = Color.Parse("#E24B4A"); // Red
            }
        }
        else
        {
            // The Task.Delay finished first, meaning the 6 seconds expired!
            ConnectionStatusText = "Connection timed out. Remote server is unreachable.";
            ConnectionStatusColor = Color.Parse("#E24B4A"); // Red
        }
    }
    catch (Exception ex)
    {
        ConnectionStatusText = $"Error: {ex.Message}";
        ConnectionStatusColor = Color.Parse("#E24B4A");
    }
}

    private async Task RunLoadTablesWorkflowAsync()
    {
        if (string.IsNullOrEmpty(SelectedSchema))
        {
            ConnectionStatusText = "PLease a valid schema first";
            ConnectionStatusColor = Color.Parse("#EF9F27");
            return;
        }
        
        ConnectionStatusText = $"Loading tables & views for '{SelectedSchema}'...";
        ConnectionStatusColor = Color.Parse("#EF9F27");

        var currentConfig = new DatabaseConfig
        {
            Host = Host,
            Port = Port,
            DatabaseName = DatabaseName,
            Username = Username,
            Password = Password
        };

        var dbService = new DatabaseRepository(currentConfig);
        
        try
        {
            // Fetch metadata from DB asynchronously
            var loadedObjects = await dbService.GetTablesAndViewsAsync(SelectedSchema);

            DatabaseObjects.Clear();
            foreach (var dbObj in loadedObjects)
            {
                DatabaseObjects.Add(dbObj);
            }
            
            ViewBuilder.DbConfig = new DatabaseConfig
            {
                Host = Host, Port = Port, DatabaseName = DatabaseName,
                Username = Username, Password = Password
            };
            ViewBuilder.Schema = SelectedSchema;
            ViewBuilder.AvailableTables.Clear();
            foreach (var obj in DatabaseObjects)
                ViewBuilder.AvailableTables.Add(obj);

            ConnectionStatusText = $"Loaded {DatabaseObjects.Count} objects from '{SelectedSchema}'";
            ConnectionStatusColor = Color.Parse("#1D9E75");
        }
        catch (Exception ex)
        {
            ConnectionStatusText = $"Failed to load structural elements: {ex.Message}";
            ConnectionStatusColor = Color.Parse("#E24B4A");
        }
    }
    
    
    
// =========================== TAB 2 - Script Gen ============================

    public ObservableCollection<string> ScriptTypes { get; } = new() { "Master procedure", "Detail procedure", "Select procedure" };
    public ObservableCollection<string> Templates { get; } = new();
    public ObservableCollection<ColumnViewModel> Columns { get; } = new();
    public ObservableCollection<ColumnViewModel> LineColumns { get; } = new();

    private string _selectedScriptType = "Master procedure";
    public string SelectedScriptType { get => _selectedScriptType; set => this.RaiseAndSetIfChanged(ref _selectedScriptType, value); }

    private string _selectedTemplate = "postgresql_master.sql";
    public string SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTemplate, value);
            LoadSelectedTemplate();
        }
    }

    private string _procedureName = "";
    public string ProcedureName { get => _procedureName; set => this.RaiseAndSetIfChanged(ref _procedureName, value); }

    private DatabaseObject? _selectedScriptTable = new DatabaseObject();
    public DatabaseObject? SelectedScriptTable 
    { 
        get => _selectedScriptTable;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedScriptTable, value);

            //  Fixed: Added '!' to check if table name is valid and NOT empty
            if (value != null && !string.IsNullOrWhiteSpace(value.Name))
            {
                // 1. Auto-generate procedure name
                ProcedureName = $"pr_{value.Name}_save";
        
                // 2. Fire background metadata routine
                _ = FetchColumnsForSelectedTableAsync(value.Name);
            }
        }
    }

    private string _generatedSql = "";
    public string GeneratedSql { get => _generatedSql; set => this.RaiseAndSetIfChanged(ref _generatedSql, value); }

    private string _templateContent = "";
    public string TemplateContent
    {
        get => _templateContent;
        set
        {
            this.RaiseAndSetIfChanged(ref _templateContent, value);
            this.RaisePropertyChanged(nameof(TemplateRequiresLineTable));
        }
    }

    public bool TemplateRequiresLineTable
        => TemplateContent.Contains("{{lineTable}}", StringComparison.OrdinalIgnoreCase)
           || TemplateContent.Contains("{{line_", StringComparison.OrdinalIgnoreCase);

    private string _templateStatusText = "";
    public string TemplateStatusText { get => _templateStatusText; set => this.RaiseAndSetIfChanged(ref _templateStatusText, value); }

    public IClipboard? Clipboard { get; set; }

    public ReactiveCommand<Unit, Unit> AddColumnCommand { get; }
    public ReactiveCommand<ColumnViewModel, Unit> RemoveColumnCommand { get; }
    public ReactiveCommand<Unit, Unit> GenerateSqlCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySqlCommand { get; }
    public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowGeneratedSqlCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowTemplateEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveTemplateCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadTemplateCommand { get; }

    private bool _showGeneratedSql = true;
    public bool ShowGeneratedSql
    {
        get => _showGeneratedSql;
        set
        {
            this.RaiseAndSetIfChanged(ref _showGeneratedSql, value);
            this.RaisePropertyChanged(nameof(ShowTemplateEditor));
        }
    }

    private DatabaseObject? _selectedLineTable;
    public DatabaseObject? SelectedLineTable
    {
        get => _selectedLineTable;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLineTable, value);

            if (value != null && !string.IsNullOrWhiteSpace(value.Name))
                _ = FetchLineColumnsForSelectedTableAsync(value.Name);
            else
                LineColumns.Clear();
        }
    }
    public bool ShowTemplateEditor => !ShowGeneratedSql;

    private string TemplatesDirectory => Path.Combine(AppContext.BaseDirectory, "Templates");

    private void LoadTemplates()
    {
        Templates.Clear();

        if (Directory.Exists(TemplatesDirectory))
        {
            foreach (var file in Directory.GetFiles(TemplatesDirectory, "*.sql").OrderBy(Path.GetFileName))
                Templates.Add(Path.GetFileName(file));
        }

        if (Templates.Count == 0)
            Templates.Add(SelectedTemplate);

        if (!Templates.Contains(SelectedTemplate))
            SelectedTemplate = Templates[0];
    }

    private string GetSelectedTemplatePath()
        => Path.Combine(TemplatesDirectory, SelectedTemplate);

    private void LoadSelectedTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedTemplate)) return;

        var templatePath = GetSelectedTemplatePath();
        if (!File.Exists(templatePath))
        {
            TemplateContent = "";
            TemplateStatusText = $"Template not found: {templatePath}";
            return;
        }

        try
        {
            TemplateContent = File.ReadAllText(templatePath);
            TemplateStatusText = $"Loaded {SelectedTemplate}";
        }
        catch (Exception ex)
        {
            TemplateContent = "";
            TemplateStatusText = $"Failed to load template: {ex.Message}";
        }
    }

    private void SaveSelectedTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedTemplate)) return;

        try
        {
            Directory.CreateDirectory(TemplatesDirectory);
            File.WriteAllText(GetSelectedTemplatePath(), TemplateContent);
            TemplateStatusText = $"Saved {SelectedTemplate}";
        }
        catch (Exception ex)
        {
            TemplateStatusText = $"Failed to save template: {ex.Message}";
        }
    }

    private void GenerateSqlFromTemplate()
    {
        var templateContent = TemplateContent;
        if (string.IsNullOrWhiteSpace(templateContent))
        {
            GeneratedSql = $"-- Template is empty: {SelectedTemplate}";
            ShowGeneratedSql = true;
            return;
        }

        GeneratedSql = TemplateRequiresLineTable
            ? SqlBuilder.GenerateScript(
                templateContent,
                SelectedSchema,
                SelectedScriptTable?.Name ?? "",
                SelectedLineTable?.Name ?? "",
                ContextCol,
                ProcedureName,
                Columns,
                LineColumns)
            : SqlBuilder.GenerateScript(
                templateContent,
                SelectedSchema,
                SelectedScriptTable?.Name ?? "",
                ContextCol,
                ProcedureName,
                Columns);

        ShowGeneratedSql = true;
        
        // ── AutoSave ──
        if (AppSettingsViewModel.Current.AutoSaveEnabled)
        {
            var savePath = AppSettingsViewModel.Current.QuerySavePath;
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                var fileName = $"{ProcedureName}_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
                var fullPath = Path.Combine(savePath, fileName);
                try { File.WriteAllText(fullPath, GeneratedSql); }
                catch { /* silent — don't interrupt generation */ }
            }
        }
    }
    
    private async Task FetchColumnsForSelectedTableAsync(string tableName)
    {
        if (string.IsNullOrWhiteSpace(SelectedSchema)) return;

        var currentConfig = new DatabaseConfig
        {
            Host = Host, Port = Port, DatabaseName = DatabaseName, Username = Username, Password = Password
        };

        var dbService = new DatabaseRepository(currentConfig);

        try
        {
            var rawColumns = await dbService.GetColumnsAsync(SelectedSchema, tableName);
    
            Columns.Clear();
            foreach (var col in rawColumns)
            {
                Columns.Add(new ColumnViewModel
                {
                    ColumnName   = col.ColumnName,
                    DataType     = col.DataType,
                    UseColumn    = true, // Map as checked by default for speed
                    IsNull       = col.IsNull,
                    IsPK         = col.IsPK,
                    IsID         = col.IsID,
                    IsArray      = col.IsArray,
                    DefaultValue = col.DefaultValue ?? string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            GeneratedSql = $"-- Error loading columns: {ex.Message}";
        }
    }

    private async Task FetchLineColumnsForSelectedTableAsync(string tableName)
    {
        if (string.IsNullOrWhiteSpace(SelectedSchema)) return;

        var currentConfig = new DatabaseConfig
        {
            Host = Host, Port = Port, DatabaseName = DatabaseName, Username = Username, Password = Password
        };

        var dbService = new DatabaseRepository(currentConfig);

        try
        {
            var rawColumns = await dbService.GetColumnsAsync(SelectedSchema, tableName);

            LineColumns.Clear();
            foreach (var col in rawColumns)
            {
                LineColumns.Add(new ColumnViewModel
                {
                    ColumnName   = col.ColumnName,
                    DataType     = col.DataType,
                    UseColumn    = true,
                    IsNull       = col.IsNull,
                    IsPK         = col.IsPK,
                    IsID         = col.IsID,
                    IsArray      = col.IsArray,
                    DefaultValue = col.DefaultValue ?? string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            GeneratedSql = $"-- Error loading line columns: {ex.Message}";
        }
    }
}

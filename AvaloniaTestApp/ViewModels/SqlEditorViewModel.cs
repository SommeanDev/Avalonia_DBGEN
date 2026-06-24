using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;
using AvaloniaTestApp.Models;
using ReactiveUI;

namespace AvaloniaTestApp.ViewModels;

public class SqlEditorViewModel : ReactiveObject
{
    /// <summary>
    /// Supplied by MainWindowViewModel so this VM can build its own DatabaseRepository
    /// using whatever connection details are currently configured on the Database tab.
    /// </summary>
    public Func<DatabaseConfig>? GetCurrentConfig { get; init; }

    // ----- Query text -----
    private string _sqlQuery = "";
    public string SqlQuery
    {
        get => _sqlQuery;
        set => this.RaiseAndSetIfChanged(ref _sqlQuery, value);
    }

    // ----- Safety toggles (default OFF / blocked, per plan) -----
    private bool _allowDrop;
    public bool AllowDrop
    {
        get => _allowDrop;
        set => this.RaiseAndSetIfChanged(ref _allowDrop, value);
    }

    private bool _allowDelete;
    public bool AllowDelete
    {
        get => _allowDelete;
        set => this.RaiseAndSetIfChanged(ref _allowDelete, value);
    }

    private bool _allowTruncate;
    public bool AllowTruncate
    {
        get => _allowTruncate;
        set => this.RaiseAndSetIfChanged(ref _allowTruncate, value);
    }

    // ----- Status -----
    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private Color _statusColor = Color.Parse("#888780");
    public Color StatusColor
    {
        get => _statusColor;
        set => this.RaiseAndSetIfChanged(ref _statusColor, value);
    }

    private string _executionTime = "";
    public string ExecutionTime
    {
        get => _executionTime;
        set => this.RaiseAndSetIfChanged(ref _executionTime, value);
    }

    private string _rowCountText = "";
    public string RowCountText
    {
        get => _rowCountText;
        set => this.RaiseAndSetIfChanged(ref _rowCountText, value);
    }

    private bool _isExecuting;
    public bool IsExecuting
    {
        get => _isExecuting;
        private set => this.RaiseAndSetIfChanged(ref _isExecuting, value);
    }

    // ----- Results -----
    public ObservableCollection<string> ResultColumns { get; } = new();
    public ObservableCollection<object[]> ResultRows { get; } = new();

    // Per-statement breakdown, useful for multi-statement batches so the user can see
    // which statement(s) were blocked or what happened to each one.
    public ObservableCollection<SqlStatementResult> StatementResults { get; } = new();

    public ReactiveCommand<Unit, Unit> ExecuteQueryCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearQueryCommand { get; }

    public SqlEditorViewModel SqlEditor { get; }
    
    public SqlEditorViewModel()
    {
        ExecuteQueryCommand = ReactiveCommand.CreateFromTask(ExecuteAsync); // ← remove outputScheduler

        ClearQueryCommand = ReactiveCommand.Create(() =>
        {
            SqlQuery = "";
            ResultColumns.Clear();
            ResultRows.Clear();
            StatementResults.Clear();
            StatusMessage = "Ready";
            StatusColor = Color.Parse("#888780");
            ExecutionTime = "";
            RowCountText = "";
        });
    }

    private async Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlQuery))
        {
            StatusMessage = "Nothing to execute — query is empty.";
            StatusColor = Color.Parse("#EF9F27");
            return;
        }

        if (GetCurrentConfig is null)
        {
            StatusMessage = "No database configuration available.";
            StatusColor = Color.Parse("#E24B4A");
            return;
        }

        IsExecuting = true;
        StatusMessage = "Executing...";
        StatusColor = Color.Parse("#EF9F27");
        ResultColumns.Clear();
        ResultRows.Clear();
        StatementResults.Clear();
        ExecutionTime = "";
        RowCountText = "";

        var config = GetCurrentConfig();
        var repo = new DatabaseRepository(config);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var batch = await repo.ExecuteQueryAsync(SqlQuery, AllowDrop, AllowDelete, AllowTruncate);
            stopwatch.Stop();
            ExecutionTime = $"{stopwatch.ElapsedMilliseconds} ms";

            foreach (var stmt in batch.Statements)
                StatementResults.Add(stmt);

            if (!batch.Success)
            {
                StatusMessage = batch.Message;
                StatusColor = Color.Parse("#E24B4A");
                RowCountText = "";
                return;
            }

            var lastResultSet = batch.LastResultSet;
            if (lastResultSet is { Columns.Count: > 0 })
            {
                foreach (var col in lastResultSet.Columns)
                    ResultColumns.Add(col);
                foreach (var row in lastResultSet.Rows)
                    ResultRows.Add(row);

                RowCountText = $"{lastResultSet.Rows.Count} row(s) returned";
            }
            else if (lastResultSet is not null)
            {
                RowCountText = $"{lastResultSet.RowsAffected} row(s) affected";
            }

            StatusMessage = batch.Message;
            StatusColor = Color.Parse("#1D9E75");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ExecutionTime = $"{stopwatch.ElapsedMilliseconds} ms";
            StatusMessage = $"Unexpected error: {ex.Message}";
            StatusColor = Color.Parse("#E24B4A");
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
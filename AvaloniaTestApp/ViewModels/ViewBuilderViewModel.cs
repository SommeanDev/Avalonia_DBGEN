using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using AvaloniaTestApp.Models;
using AvaloniaTestApp.Services;
using ReactiveUI;

namespace AvaloniaTestApp.ViewModels;

public class ViewBuilderViewModel : ReactiveObject
{
    public ObservableCollection<DatabaseObject> AvailableTables { get; } = new();

    public DatabaseConfig? DbConfig { get; set; }
    public string Schema { get; set; } = string.Empty;

    // ═══════════════════════════════════════════════════════════════════════
    // Properties
    // ═══════════════════════════════════════════════════════════════════════

    private ObservableCollection<ViewBuilderTable> _tables = new();
    public ObservableCollection<ViewBuilderTable> Tables
    {
        get => _tables;
        set => this.RaiseAndSetIfChanged(ref _tables, value);
    }

    private string _viewName = "vw_custom_view";
    public string ViewName
    {
        get => _viewName;
        set => this.RaiseAndSetIfChanged(ref _viewName, value);
    }

    private DatabaseObject? _selectedTableToAdd;
    public DatabaseObject? SelectedTableToAdd
    {
        get => _selectedTableToAdd;
        set => this.RaiseAndSetIfChanged(ref _selectedTableToAdd, value);
    }

    private ObservableCollection<JoinCondition> _joinConditions = new();
    public ObservableCollection<JoinCondition> JoinConditions
    {
        get => _joinConditions;
        set => this.RaiseAndSetIfChanged(ref _joinConditions, value);
    }

    private ObservableCollection<WhereCondition> _whereConditions = new();
    public ObservableCollection<WhereCondition> WhereConditions
    {
        get => _whereConditions;
        set => this.RaiseAndSetIfChanged(ref _whereConditions, value);
    }

    private ObservableCollection<CteNode> _cteNodes = new();
    public ObservableCollection<CteNode> CteNodes
    {
        get => _cteNodes;
        set => this.RaiseAndSetIfChanged(ref _cteNodes, value);
    }

    private bool _isSqlPanelOpen;
    public bool IsSqlPanelOpen
    {
        get => _isSqlPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isSqlPanelOpen, value);
    }

    private string _generatedSql = string.Empty;
    public string GeneratedSql
    {
        get => _generatedSql;
        set => this.RaiseAndSetIfChanged(ref _generatedSql, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private bool _isCopied;
    public bool IsCopied
    {
        get => _isCopied;
        set => this.RaiseAndSetIfChanged(ref _isCopied, value);
    }

    public ReactiveCommand<Unit, Unit> AddTableCommand { get; }
    public ReactiveCommand<ViewBuilderTable, Unit> RemoveTableCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCteNodeCommand { get; }
    public ReactiveCommand<CteNode, Unit> RemoveCteNodeCommand { get; }
    public ReactiveCommand<CteNode, Unit> AddCteConditionCommand { get; }
    public ReactiveCommand<Unit, Unit> AddWhereConditionCommand { get; }
    public ReactiveCommand<WhereCondition, Unit> RemoveWhereConditionCommand { get; }
    public ReactiveCommand<Unit, Unit> GenerateSqlCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseSqlPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySqlCommand { get; }
    public ReactiveCommand<Unit, Unit> SendToScriptGeneratorCommand { get; }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor
    // ═══════════════════════════════════════════════════════════════════════

    public ViewBuilderViewModel()
    {
        // FIX 1: Explicit cast to bool avoids the ambiguous WhenAnyValue overload
        var canAdd = this.WhenAnyValue(x => x.SelectedTableToAdd)
                         .Select(t => t != null);

        AddTableCommand = ReactiveCommand.CreateFromTask(
            AddTableAsync,
            canAdd,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        RemoveTableCommand = ReactiveCommand.Create<ViewBuilderTable>(
            RemoveTable,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        AddCteNodeCommand = ReactiveCommand.Create(
            AddCteNode,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        RemoveCteNodeCommand = ReactiveCommand.Create<CteNode>(
            RemoveCteNode,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        AddCteConditionCommand = ReactiveCommand.Create<CteNode>(
            AddCteCondition,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        AddWhereConditionCommand = ReactiveCommand.Create(
            AddWhereCondition,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        RemoveWhereConditionCommand = ReactiveCommand.Create<WhereCondition>(
            condition => WhereConditions.Remove(condition),
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        GenerateSqlCommand = ReactiveCommand.Create(
            GenerateSql,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        CloseSqlPanelCommand = ReactiveCommand.Create(
            () => { IsSqlPanelOpen = false; },
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        CopySqlCommand = ReactiveCommand.CreateFromTask(
            CopySqlAsync,
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);

        SendToScriptGeneratorCommand = ReactiveCommand.Create(
            () => { },
            outputScheduler: Avalonia.ReactiveUI.AvaloniaScheduler.Instance);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Add table
    // ═══════════════════════════════════════════════════════════════════════

    private async Task AddTableAsync()
    {
        if (SelectedTableToAdd is null || DbConfig is null) return;

        if (Tables.Any(t => t.TableName == SelectedTableToAdd.Name))
        {
            StatusText = $"'{SelectedTableToAdd.Name}' is already on the canvas.";
            return;
        }

        StatusText = $"Loading columns for {SelectedTableToAdd.Name}...";

        try
        {
            var dbService  = new DatabaseRepository(DbConfig);
            var rawColumns = await dbService.GetColumnsAsync(Schema, SelectedTableToAdd.Name);

            int    position = Tables.Count + 1;
            string alias    = ViewBuilderTable.BuildAlias(SelectedTableToAdd.Name, position);
            double xOffset  = Tables.Count * 280;

            var table = new ViewBuilderTable
            {
                TableName = SelectedTableToAdd.Name,
                Alias     = alias,
                X         = 40 + xOffset,
                Y         = 60,
            };

            foreach (var col in rawColumns)
            {
                table.Columns.Add(new ViewColumn
                {
                    ColumnName   = col.ColumnName,
                    DataType     = col.DataType,
                    IsSelected   = true,
                    IsPrimaryKey = col.IsPK,
                    IsForeignKey = col.IsID,
                });
            }

            Tables.Add(table);
            RefreshWhereConditionColumns();
            RefreshCteNodeSources();

            if (Tables.Count > 1)
            {
                var baseTable = Tables[0];
                var join = new JoinCondition
                {
                    JoinType        = "LEFT JOIN",
                    LeftTableAlias  = alias,
                    LeftColumn      = string.Empty,
                    RightTableAlias = baseTable.Alias,
                    RightColumn     = string.Empty,
                };

                foreach (var col in table.Columns)
                    join.LeftColumns.Add(col.ColumnName);
                foreach (var col in baseTable.Columns)
                    join.RightColumns.Add(col.ColumnName);

                JoinConditions.Add(join);
            }

            StatusText = $"Added '{table.TableName}' · {table.Columns.Count} columns.";
            SelectedTableToAdd = null;
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load columns: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Remove table
    // ═══════════════════════════════════════════════════════════════════════

    private void RemoveTable(ViewBuilderTable table)
    {
        Tables.Remove(table);

        var toRemove = JoinConditions
            .Where(j => j.LeftTableAlias == table.Alias || j.RightTableAlias == table.Alias)
            .ToList();

        foreach (var j in toRemove)
            JoinConditions.Remove(j);

        RefreshWhereConditionColumns();
        RefreshCteNodeSources();

        StatusText = $"Removed '{table.TableName}'.";
    }

    private void AddCteNode()
    {
        if (Tables.Count == 0)
        {
            StatusText = "Add a table before adding a CTE.";
            return;
        }

        var source = Tables[0];
        var node = new CteNode
        {
            Name = $"cte_{source.TableName}",
            SourceTableAlias = source.Alias,
            X = 80 + (CteNodes.Count * 280),
            Y = 430,
        };

        node.PropertyChanged += OnCteNodePropertyChanged;
        PopulateCteSources(node);
        RefreshCteOutputColumns(node);
        AddCteCondition(node);

        CteNodes.Add(node);
        StatusText = $"Added CTE '{node.Name}'.";
    }

    private void RemoveCteNode(CteNode node)
    {
        node.PropertyChanged -= OnCteNodePropertyChanged;
        CteNodes.Remove(node);
        StatusText = $"Removed CTE '{node.Name}'.";
    }

    private void AddCteCondition(CteNode node)
    {
        var columns = GetColumnsForAlias(node.SourceTableAlias).ToList();
        if (columns.Count == 0) return;

        var condition = new WhereCondition
        {
            SelectedColumn = columns[0],
            Operator = "=",
        };

        foreach (var column in columns)
            condition.AvailableColumns.Add(column);

        node.Conditions.Add(condition);
    }

    private void OnCteNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is CteNode node && e.PropertyName == nameof(CteNode.SourceTableAlias))
        {
            RefreshCteOutputColumns(node);
            RefreshCteConditionColumns(node);
        }
    }

    private void AddWhereCondition()
    {
        var columns = GetSelectableColumns().ToList();
        if (columns.Count == 0)
        {
            StatusText = "Add a table before adding a condition.";
            return;
        }

        var condition = new WhereCondition
        {
            LogicalOperator = WhereConditions.Count == 0 ? "AND" : "AND",
            SelectedColumn = columns[0],
            Operator = "=",
        };

        foreach (var column in columns)
            condition.AvailableColumns.Add(column);

        WhereConditions.Add(condition);
        StatusText = "Added WHERE condition.";
    }

    private IEnumerable<string> GetSelectableColumns()
    {
        return Tables.SelectMany(table =>
            table.Columns.Select(column => $"{table.Alias}.{column.ColumnName}"));
    }

    private IEnumerable<string> GetColumnsForAlias(string alias)
    {
        var table = Tables.FirstOrDefault(table => table.Alias == alias);
        return table?.Columns.Select(column => $"{alias}.{column.ColumnName}") ?? Enumerable.Empty<string>();
    }

    private void RefreshWhereConditionColumns()
    {
        var columns = GetSelectableColumns().ToList();

        foreach (var condition in WhereConditions.ToList())
        {
            condition.AvailableColumns.Clear();
            foreach (var column in columns)
                condition.AvailableColumns.Add(column);

            if (!columns.Contains(condition.SelectedColumn))
            {
                if (columns.Count == 0)
                    WhereConditions.Remove(condition);
                else
                    condition.SelectedColumn = columns[0];
            }
        }
    }

    private void RefreshCteNodeSources()
    {
        foreach (var node in CteNodes.ToList())
        {
            PopulateCteSources(node);

            if (!Tables.Any(table => table.Alias == node.SourceTableAlias))
            {
                var nextSource = Tables.FirstOrDefault();
                if (nextSource is null)
                {
                    RemoveCteNode(node);
                    continue;
                }

                node.SourceTableAlias = nextSource.Alias;
            }

            RefreshCteConditionColumns(node);
            RefreshCteOutputColumns(node);
        }
    }

    private void PopulateCteSources(CteNode node)
    {
        node.AvailableSourceTables.Clear();
        foreach (var table in Tables)
            node.AvailableSourceTables.Add(table.Alias);
    }

    private void RefreshCteConditionColumns(CteNode node)
    {
        var columns = GetColumnsForAlias(node.SourceTableAlias).ToList();
        foreach (var condition in node.Conditions.ToList())
        {
            condition.AvailableColumns.Clear();
            foreach (var column in columns)
                condition.AvailableColumns.Add(column);

            if (!columns.Contains(condition.SelectedColumn))
                condition.SelectedColumn = columns.FirstOrDefault() ?? string.Empty;
        }
    }

    private void RefreshCteOutputColumns(CteNode node)
    {
        var source = Tables.FirstOrDefault(table => table.Alias == node.SourceTableAlias);
        if (source is null)
        {
            node.OutputColumns.Clear();
            return;
        }

        var previous = node.OutputColumns
            .ToDictionary(column => column.ColumnName, column => column.IsSelected, StringComparer.OrdinalIgnoreCase);

        node.OutputColumns.Clear();
        foreach (var sourceColumn in source.Columns)
        {
            node.OutputColumns.Add(new ViewColumn
            {
                ColumnName = sourceColumn.ColumnName,
                DataType = sourceColumn.DataType,
                IsPrimaryKey = sourceColumn.IsPrimaryKey,
                IsForeignKey = sourceColumn.IsForeignKey,
                IsSelected = !previous.TryGetValue(sourceColumn.ColumnName, out var wasSelected) || wasSelected,
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generate SQL
    // ═══════════════════════════════════════════════════════════════════════

    private void GenerateSql()
    {
        if (Tables.Count == 0)
        {
            StatusText = "Add at least one table first.";
            return;
        }

        var sb           = new StringBuilder();
        var schemaPrefix = string.IsNullOrWhiteSpace(Schema) ? string.Empty : $"{Schema}.";
        var cteNodes = CteNodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Name)
                           && Tables.Any(table => table.Alias == node.SourceTableAlias))
            .ToList();

        sb.AppendLine($"CREATE OR REPLACE VIEW {schemaPrefix}{ViewName} AS");

        if (cteNodes.Count > 0)
        {
            sb.AppendLine("WITH");
            for (int i = 0; i < cteNodes.Count; i++)
            {
                AppendCteSql(sb, cteNodes[i]);
                sb.AppendLine(i < cteNodes.Count - 1 ? ")," : ")");
            }
        }

        sb.AppendLine("SELECT");

        var selectCols = Tables
            .SelectMany(t => GetFinalSelectableColumns(t)
                .Select(c => $"    {t.Alias}.{c.ColumnName} AS {t.TableName}_{c.ColumnName}"))
            .ToList();

        for (int i = 0; i < selectCols.Count; i++)
            sb.AppendLine(selectCols[i] + (i < selectCols.Count - 1 ? "," : ""));

        var first = Tables[0];
        sb.AppendLine($"FROM {GetQueryableName(first)} {first.Alias}");

        foreach (var join in JoinConditions)
        {
            var leftTable  = Tables.FirstOrDefault(t => t.Alias == join.LeftTableAlias);
            var rightTable = Tables.FirstOrDefault(t => t.Alias == join.RightTableAlias);
            if (leftTable is null || rightTable is null) continue;

            bool hasCondition = !string.IsNullOrWhiteSpace(join.LeftColumn)
                             && !string.IsNullOrWhiteSpace(join.RightColumn);

            if (hasCondition)
                sb.AppendLine($"{join.JoinType} {GetQueryableName(leftTable)} {join.LeftTableAlias} ON {join.LeftTableAlias}.{join.LeftColumn} = {join.RightTableAlias}.{join.RightColumn}");
            else
                sb.AppendLine($"{join.JoinType} {GetQueryableName(leftTable)} {join.LeftTableAlias}  -- ON condition not configured");
        }

        var whereLines = WhereConditions
            .Select(condition => new
            {
                condition.LogicalOperator,
                Clause = BuildWhereClause(condition)
            })
            .Where(condition => !string.IsNullOrWhiteSpace(condition.Clause))
            .ToList();

        if (whereLines.Count > 0)
        {
            sb.AppendLine("WHERE");
            for (int i = 0; i < whereLines.Count; i++)
            {
                var prefix = i == 0 ? "    " : $"    {whereLines[i].LogicalOperator} ";
                sb.AppendLine(prefix + whereLines[i].Clause);
            }
        }

        GeneratedSql   = sb.ToString();
        IsSqlPanelOpen = true;
    }

    private string GetQueryableName(ViewBuilderTable table)
    {
        return CteNodes.FirstOrDefault(node => node.SourceTableAlias == table.Alias
                                               && !string.IsNullOrWhiteSpace(node.Name))
                   ?.Name
               ?? table.TableName;
    }

    private IEnumerable<ViewColumn> GetFinalSelectableColumns(ViewBuilderTable table)
    {
        var cte = CteNodes.FirstOrDefault(node => node.SourceTableAlias == table.Alias
                                                  && !string.IsNullOrWhiteSpace(node.Name));

        if (cte is null)
            return table.Columns.Where(column => column.IsSelected);

        var cteColumnNames = cte.OutputColumns
            .Where(column => column.IsSelected)
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return table.Columns.Where(column => column.IsSelected && cteColumnNames.Contains(column.ColumnName));
    }

    private void AppendCteSql(StringBuilder sb, CteNode node)
    {
        var source = Tables.First(table => table.Alias == node.SourceTableAlias);
        sb.AppendLine($"{node.Name} AS (");
        var outputColumns = node.OutputColumns
            .Where(column => column.IsSelected)
            .Select(column => column.ColumnName)
            .ToList();

        var requiredColumns = GetColumnsRequiredOutsideCte(source.Alias);
        foreach (var requiredColumn in requiredColumns)
        {
            if (!outputColumns.Contains(requiredColumn, StringComparer.OrdinalIgnoreCase))
                outputColumns.Add(requiredColumn);
        }

        if (outputColumns.Count == 0)
            outputColumns.AddRange(source.Columns.Select(column => column.ColumnName));

        sb.AppendLine("    SELECT");
        for (int i = 0; i < outputColumns.Count; i++)
        {
            var suffix = i < outputColumns.Count - 1 ? "," : string.Empty;
            sb.AppendLine($"        {source.Alias}.{outputColumns[i]}{suffix}");
        }
        sb.AppendLine($"    FROM {source.TableName} {source.Alias}");

        var clauses = node.Conditions
            .Select(BuildWhereClause)
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .ToList();

        if (clauses.Count > 0)
        {
            sb.AppendLine("    WHERE");
            for (int i = 0; i < clauses.Count; i++)
            {
                var logical = i == 0 ? string.Empty : $"{node.Conditions[i].LogicalOperator} ";
                sb.AppendLine($"        {logical}{clauses[i]}");
            }
        }
    }

    private IEnumerable<string> GetColumnsRequiredOutsideCte(string tableAlias)
    {
        foreach (var join in JoinConditions)
        {
            if (join.LeftTableAlias == tableAlias && !string.IsNullOrWhiteSpace(join.LeftColumn))
                yield return join.LeftColumn;

            if (join.RightTableAlias == tableAlias && !string.IsNullOrWhiteSpace(join.RightColumn))
                yield return join.RightColumn;
        }

        foreach (var condition in WhereConditions)
        {
            var prefix = $"{tableAlias}.";
            if (condition.SelectedColumn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return condition.SelectedColumn[prefix.Length..];
        }
    }

    private static string BuildWhereClause(WhereCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.SelectedColumn)
            || string.IsNullOrWhiteSpace(condition.Operator))
            return string.Empty;

        if (condition.Operator is "IS NULL" or "IS NOT NULL")
            return $"{condition.SelectedColumn} {condition.Operator}";

        if (string.IsNullOrWhiteSpace(condition.Value))
            return string.Empty;

        return $"{condition.SelectedColumn} {condition.Operator} {FormatSqlValue(condition.Operator, condition.Value)}";
    }

    private static string FormatSqlValue(string op, string value)
    {
        var trimmed = value.Trim();

        if (op == "IN")
            return trimmed.StartsWith("(") ? trimmed : $"({trimmed})";

        if (trimmed.StartsWith("'")
            || trimmed.StartsWith(":")
            || trimmed.StartsWith("@")
            || trimmed.StartsWith("$")
            || decimal.TryParse(trimmed, out _)
            || string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("CURRENT_", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"'{trimmed.Replace("'", "''")}'";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Copy SQL  — FIX 3: use TopLevel.Clipboard property, not SetTextAsync
    // ═══════════════════════════════════════════════════════════════════════

    private int _copyFeedbackCount = 0;

    private async Task CopySqlAsync()
    {
        if (string.IsNullOrEmpty(GeneratedSql)) return;

        var desktop    = Avalonia.Application.Current?.ApplicationLifetime
                             as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        if (mainWindow is null) return;

        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(mainWindow)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(GeneratedSql);
            IsCopied = true;

            int currentCount = ++_copyFeedbackCount;
            await Task.Delay(2000);
            if (currentCount == _copyFeedbackCount)
            {
                IsCopied = false;
            }
        }
    }
}

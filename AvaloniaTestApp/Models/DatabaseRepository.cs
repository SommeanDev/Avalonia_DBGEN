using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AvaloniaTestApp.ViewModels;
using Npgsql;

namespace AvaloniaTestApp.Models;

/// <summary>
/// Result of executing a single statement within a SQL Editor batch.
/// </summary>
public class SqlStatementResult
{
    public string Statement { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Blocked { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<object[]> Rows { get; set; } = new();
    public int RowsAffected { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Aggregate result for a full SQL Editor batch (one or more ';'-separated statements).
/// </summary>
public class SqlBatchResult
{
    public bool Success { get; set; }
    public List<SqlStatementResult> Statements { get; set; } = new();
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Convenience accessor: columns/rows from the last statement that actually returned rows.
    /// Used by the SQL Editor grid, which displays only the final result set.
    /// </summary>
    public SqlStatementResult? LastResultSet
    {
        get
        {
            for (int i = Statements.Count - 1; i >= 0; i--)
            {
                if (Statements[i].Columns.Count > 0)
                    return Statements[i];
            }
            return Statements.Count > 0 ? Statements[^1] : null;
        }
    }
}

public class DatabaseRepository
{
    private readonly string _connectionString;
    
    public DatabaseRepository(DatabaseConfig config)
    {
        // Enforce hard connection timeouts directly inside the connection parameters
        _connectionString = $"Host={config.Host};Port={config.Port};Database={config.DatabaseName};" +
                            $"Username={config.Username};Password={config.Password};" +
                            $"Timeout=5;Command Timeout=5;";
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            // Use the modern NpgsqlDataSource which natively handles socket timeouts better in modern .NET
            await using var dataSource = NpgsqlDataSource.Create(_connectionString);
            await using var conn = await dataSource.OpenConnectionAsync();
            
            return (true, "Connection successful");
        }
        catch (NpgsqlException nex)
        {
            return (false, $"Database error: {nex.Message}");
        }
        catch (TimeoutException tex)
        {
            return (false, $"Network Timeout: Server took too long to respond. {tex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Socket Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetch schemas from the database instance
    /// </summary>
    public async Task<List<string>> GetSchemaAsync()
    {
        try
        {
            var schemas = new List<string>();
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            
            var query = @"SELECT schema_name 
                      FROM information_schema.schemata 
                      WHERE schema_name NOT IN ('pg_catalog', 'information_schema')
                      ORDER BY schema_name;";
            
            using var cmd = new NpgsqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                schemas.Add(reader.GetString(0));
            }
            return schemas;
        }
        catch (Exception e)
        {
            return new List<string>{e.Message};
        }
    }
    
    /// <summary>
    /// 2 & 3. Fetch both tables and views inside the selected schema
    /// </summary>
    public async Task<List<DatabaseObject>> GetTablesAndViewsAsync(string schemaName)
    {
        var dbObjects = new List<DatabaseObject>();
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var query = @"
            SELECT table_name, 'Table' AS type 
            FROM information_schema.tables 
            WHERE table_schema = @schema AND table_type = 'BASE TABLE'
            UNION ALL
            SELECT table_name, 'View' AS type 
            FROM information_schema.views 
            WHERE table_schema = @schema
            ORDER BY table_name;";

        using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("schema", schemaName);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var typeStr = reader.GetString(1);
            
            dbObjects.Add(new DatabaseObject
            {
                Name = name,
                Type = typeStr == "Table" ? DbObjectType.Table : DbObjectType.View
            });
        }
        return dbObjects;
    }

    public async Task<List<ColumnViewModel>> GetColumnsAsync(string schemaName, string tableName)
    {
        var columns = new List<ColumnViewModel>();
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var query = @"
    SELECT 
        c.column_name,
        c.data_type,
        c.is_nullable,
        c.column_default,
        CASE WHEN pk.column_name IS NOT NULL THEN true ELSE false END AS is_pk
    FROM information_schema.columns c
    LEFT JOIN (
        SELECT kcu.column_name
        FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
            ON tc.constraint_name = kcu.constraint_name
            AND tc.table_schema = kcu.table_schema
            AND tc.table_name = kcu.table_name
        WHERE tc.constraint_type = 'PRIMARY KEY'
          AND tc.table_schema = @schema
          AND tc.table_name = @table
    ) pk ON pk.column_name = c.column_name
    WHERE c.table_schema = @schema
      AND c.table_name = @table
    ORDER BY c.ordinal_position;";

        using var cmd = new NpgsqlCommand(query, conn);
    
        //  Fixed: Parameter lookup tags cleared of leading '@' symbols
        cmd.Parameters.AddWithValue("schema", schemaName);
        cmd.Parameters.AddWithValue("table", tableName);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnViewModel
            {
                ColumnName   = reader.GetString(0),
                DataType     = reader.GetString(1),
                IsNull       = reader.GetString(2) == "YES",
                //  Fixed: Defend against DBNull using empty string fallback
                DefaultValue = reader.IsDBNull(3) ? string.Empty : reader.GetValue(3).ToString(),
                IsPK         = reader.GetBoolean(4),
                UseColumn    = true,
                IsID         = false,
                IsArray      = false,
            });
        }
        return columns;
    }

    // ===================== SQL Editor =====================

    /// <summary>
    /// Splits a raw multi-statement SQL string on top-level ';' separators, ignoring
    /// semicolons that appear inside single-quoted strings, double-quoted identifiers,
    /// line comments ('--') or block comments ('/* */'). Empty/whitespace-only
    /// statements are dropped.
    /// </summary>
    public static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) return statements;

        var current = new StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                current.Append(c);
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                current.Append(c);
                if (c == '*' && next == '/')
                {
                    current.Append(next);
                    i++;
                    inBlockComment = false;
                }
                continue;
            }

            if (inSingleQuote)
            {
                current.Append(c);
                // Handle doubled '' escape inside a single-quoted string
                if (c == '\'')
                {
                    if (next == '\'') { current.Append(next); i++; }
                    else inSingleQuote = false;
                }
                continue;
            }

            if (inDoubleQuote)
            {
                current.Append(c);
                if (c == '"')
                {
                    if (next == '"') { current.Append(next); i++; }
                    else inDoubleQuote = false;
                }
                continue;
            }

            // Not inside any quote/comment — check for entry points
            if (c == '-' && next == '-')
            {
                inLineComment = true;
                current.Append(c);
                continue;
            }
            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                current.Append(c);
                continue;
            }
            if (c == '\'')
            {
                inSingleQuote = true;
                current.Append(c);
                continue;
            }
            if (c == '"')
            {
                inDoubleQuote = true;
                current.Append(c);
                continue;
            }
            if (c == ';')
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt)) statements.Add(stmt);
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail)) statements.Add(tail);

        return statements;
    }

    /// <summary>
    /// Executes a (possibly multi-statement) SQL batch inside a single transaction.
    /// Statements are checked individually against the safety toggles before any
    /// of them run; if any statement is blocked, nothing in the batch is executed.
    /// On any execution error, the whole transaction is rolled back.
    /// </summary>
    public async Task<SqlBatchResult> ExecuteQueryAsync(
        string sql,
        bool allowDrop,
        bool allowDelete,
        bool allowTruncate)
    {
        var statements = SplitStatements(sql);
        var batch = new SqlBatchResult();

        if (statements.Count == 0)
        {
            batch.Success = false;
            batch.Message = "No statement to execute.";
            return batch;
        }

        // ---- Safety pre-check: validate every statement before running any of them ----
        foreach (var stmt in statements)
        {
            var (blocked, reason) = SqlSafetyCheck.CheckStatement(stmt, allowDrop, allowDelete, allowTruncate);
            if (blocked)
            {
                batch.Statements.Add(new SqlStatementResult
                {
                    Statement = stmt,
                    Success = false,
                    Blocked = true,
                    Message = reason
                });
            }
        }

        if (batch.Statements.Count > 0)
        {
            batch.Success = false;
            batch.Message = $"Execution blocked: {batch.Statements.Count} statement(s) failed the safety check. " +
                             "Enable the relevant toggle(s) to proceed.";
            return batch;
        }

        // ---- All statements passed the safety check — execute inside a transaction ----
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync();

            try
            {
                foreach (var stmt in statements)
                {
                    var result = new SqlStatementResult { Statement = stmt };

                    using var cmd = new NpgsqlCommand(stmt, conn, tx);
                    using var reader = await cmd.ExecuteReaderAsync();

                    if (reader.FieldCount > 0)
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                            result.Columns.Add(reader.GetName(i));

                        while (await reader.ReadAsync())
                        {
                            var row = new object[reader.FieldCount];
                            reader.GetValues(row);
                            for (int i = 0; i < row.Length; i++)
                                if (row[i] == DBNull.Value) row[i] = "NULL";
                            result.Rows.Add(row);
                        }

                        result.Success = true;
                        result.Message = $"{result.Rows.Count} row(s) returned.";
                    }
                    else
                    {
                        await reader.CloseAsync();
                        result.RowsAffected = reader.RecordsAffected;
                        result.Success = true;
                        result.Message = $"{Math.Max(reader.RecordsAffected, 0)} row(s) affected.";
                    }

                    batch.Statements.Add(result);
                }

                await tx.CommitAsync();
                batch.Success = true;
                batch.Message = statements.Count == 1
                    ? "Query executed successfully."
                    : $"{statements.Count} statements executed successfully.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                batch.Success = false;
                batch.Message = $"Error: {ex.Message} — transaction rolled back, no changes were committed.";
                batch.Statements.Add(new SqlStatementResult
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }
        catch (Exception ex)
        {
            batch.Success = false;
            batch.Message = $"Connection error: {ex.Message}";
        }

        return batch;
    }
}
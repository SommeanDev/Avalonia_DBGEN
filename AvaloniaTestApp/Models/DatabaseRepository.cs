using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AvaloniaTestApp.ViewModels;
using Npgsql;

namespace AvaloniaTestApp.Models;

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
}
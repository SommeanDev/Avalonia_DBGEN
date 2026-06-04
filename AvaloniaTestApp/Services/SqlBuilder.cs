using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AvaloniaTestApp.ViewModels;

namespace AvaloniaTestApp.Services;

public static class SqlBuilder
{
    public static readonly HashSet<string> AuditColumns = new (StringComparer.OrdinalIgnoreCase)
    {
        "server_sys_date", "operation_date", "operation_id", "active_flag", "delete_flag",
        "enter_user_id", "enter_desc", "last_edit_user_id", "last_edit_desc",
        "delete_user_id", "delete_desc", "pass_flag", "pass_user_id", "pass_desc"
    };
    
    private static string QuoteIdent(string name) 
        => $"\"{name.Replace("\"", "\"\"")}\"";

    private static string QualifiedName(string schema, string table)
        => $"{QuoteIdent(schema)}.{QuoteIdent(table)}";
    
    private static string PgType(string rawType) => rawType switch
    {
        "character varying" => "varchar",
        "timestamp without time zone" => "timestamp",
        "timestamp with time zone" => "timestamptz",
        "double precision" => "float8",
        "boolean" => "bool",
        _ => rawType  // integer, numeric, text, date, etc. pass through as-is
    };
    
    private static string SqlParameter(ColumnViewModel col)
    {
        var name = $"pi_{col.ColumnName}";
        var type = PgType(col.DataType);
        return $"    IN    {name,-40} {type}";
    }

    private static string SqlArrayParameter(ColumnViewModel col)
    {
        var name = $"pi_{col.ColumnName}";
        var type = PgType(col.DataType);
        return $"    IN    {name,-40} {type}[]";
    }

    private static string SqlArrayTemplateParameter(ColumnViewModel col)
        => col.IsArray ? SqlArrayParameter(col) : SqlParameter(col);

    private static string JoinWithTrailingComma(IEnumerable<string> lines)
    {
        var materialized = lines.ToList();
        return materialized.Count == 0
            ? string.Empty
            : string.Join(",\n", materialized) + ",";
    }

    public static Dictionary<string, string> BuildValues(
        string schema,
        string table,
        string contextCol,
        string procedureName,
        IEnumerable<ColumnViewModel> columns)
    {
        var cols = columns.ToList();
        var result = new Dictionary<string, string>();

        // PK — derived from columns
        var pkCol = cols.FirstOrDefault(c => c.IsPK);

        // identity tokens
        result["schema"]          = schema;
        result["table_name"]      = table;
        result["headTable"]       = table;
        result["procName"]        = procedureName;
        result["qualified_table"] = QualifiedName(schema, table);
        result["procedure_name"]  = procedureName;
        result["table_label"]     = table.Replace("_", " ").ToUpper();
        result["head_table_upper"] = table.ToUpperInvariant();
        result["pk_col"]          = pkCol?.ColumnName ?? "";
        result["pk_type"]         = pkCol != null ? PgType(pkCol.DataType) : "";
        result["context_col"]     = contextCol;
        result["context_param"]   = $"pi_{contextCol}";

        // business columns — filtered
        var businessCols = cols.Where(c => c.UseColumn
                                           && !c.IsPK
                                           && !c.IsID
                                           && !AuditColumns.Contains(c.ColumnName)
                                           && c.ColumnName != contextCol).ToList();

        result["business_parameters"] = JoinWithTrailingComma(businessCols.Select(c => SqlParameter(c)));
        result["insert_business_columns"] = JoinWithTrailingComma(
            businessCols.Select(c => $"            {c.ColumnName}"));

        result["insert_business_values"] = JoinWithTrailingComma(
            businessCols.Select(c => $"            pi_{c.ColumnName}"));

        result["temp_business_columns"] = result["insert_business_columns"];
        result["temp_business_values"]  = result["insert_business_values"];
        result["update_business_assignments"] = JoinWithTrailingComma(
            businessCols.Select(c => $"            {c.ColumnName} = pi_{c.ColumnName}"));

        var arrayBusinessCols = businessCols.Where(c => c.IsArray).ToList();

        result["array_business_parameters"] = JoinWithTrailingComma(
            businessCols.Select(SqlArrayTemplateParameter));
        result["insert_array_business_columns"] = JoinWithTrailingComma(
            businessCols.Select(c => $"            {c.ColumnName}"));
        result["insert_array_business_values"] = JoinWithTrailingComma(
            businessCols.Select(c => c.IsArray
                ? $"    t.{c.ColumnName}"
                : $"    pi_{c.ColumnName}"));
        result["temp_array_business_columns"] = result["insert_array_business_columns"];
        result["temp_array_business_values"] = result["insert_array_business_values"];
        result["unnest_array_params"] = string.Join(",\n",
            arrayBusinessCols.Select(c => $"            pi_{c.ColumnName}"));
        result["unnest_alias_columns"] = string.Join(",\n",
            arrayBusinessCols.Select(c => $"            {c.ColumnName}"));
        result["update_array_assignments"] = JoinWithTrailingComma(
            businessCols.Select(c => $"    {c.ColumnName} = rec.{c.ColumnName}"));
        result["rec_business_values"] = JoinWithTrailingComma(
            businessCols.Select(c => $"                    rec.{c.ColumnName}"));
        return result;
    }

    public static Dictionary<string, string> BuildValues(
        string schema,
        string headTable,
        string lineTable,
        string contextCol,
        string procedureName,
        IEnumerable<ColumnViewModel> headColumns,
        IEnumerable<ColumnViewModel> lineColumns)
    {
        var result = BuildValues(schema, headTable, contextCol, procedureName, headColumns);
        var headCols = headColumns.ToList();
        var lineCols = lineColumns.ToList();

        var headPk = headCols.FirstOrDefault(c => c.IsPK)?.ColumnName
                     ?? result["pk_col"];
        var linePk = lineCols.FirstOrDefault(c => c.IsPK)?.ColumnName
                     ?? lineCols.FirstOrDefault(c => c.ColumnName.EndsWith("_id", StringComparison.OrdinalIgnoreCase))?.ColumnName
                     ?? "line_no";

        var lineBusinessCols = lineCols
            .Where(c => c.UseColumn
                        && !c.IsPK
                        && !AuditColumns.Contains(c.ColumnName)
                        && !string.Equals(c.ColumnName, contextCol, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.ColumnName, headPk, StringComparison.OrdinalIgnoreCase))
            .ToList();

        result["headTable"] = headTable;
        result["lineTable"] = lineTable;
        result["procName"] = procedureName;
        result["line_pk_col"] = linePk;
        result["line_table_upper"] = lineTable.ToUpperInvariant();
        result["line_array_parameters"] = string.Join(",\n", lineBusinessCols.Select(SqlArrayParameter));
        result["line_insert_cols_full"] = string.Join(",\n", lineBusinessCols.Select(c => $"            {c.ColumnName}"));
        result["line_insert_vals_new"] = string.Join(",\n", lineBusinessCols.Select(c => $"            u.{c.ColumnName}"));
        result["line_insert_vals_rec"] = string.Join(",\n", lineBusinessCols.Select(c => $"                        rec.{c.ColumnName}"));
        result["line_unnest_params"] = string.Join(",\n", lineBusinessCols.Select(c => $"            pi_{c.ColumnName}"));
        result["line_unnest_aliases"] = string.Join(",\n", lineBusinessCols.Select(c => $"            {c.ColumnName}"));
        result["line_update_assignments"] = string.Join(",\n", lineBusinessCols.Select(c => $"                           {c.ColumnName} = rec.{c.ColumnName}"));

        return result;
    }
    
    public static string RenderTemplate(string template, Dictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            template = template.Replace($"{{{{{key}}}}}", value);
        }
        return template;
    }
    
    public static string GenerateScript(string templateContent, string schema, string table, string contextCol, 
        string procedureName, IEnumerable<ColumnViewModel> columns)
    {
        var values = BuildValues(schema, table, contextCol, procedureName, columns);
        return RenderTemplate(templateContent, values);
    }

    public static string GenerateScript(
        string templateContent,
        string schema,
        string headTable,
        string lineTable,
        string contextCol,
        string procedureName,
        IEnumerable<ColumnViewModel> headColumns,
        IEnumerable<ColumnViewModel> lineColumns)
    {
        var values = BuildValues(schema, headTable, lineTable, contextCol, procedureName, headColumns, lineColumns);
        return RenderTemplate(templateContent, values);
    }
}

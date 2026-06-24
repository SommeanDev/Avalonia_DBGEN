using System.Text.RegularExpressions;

namespace AvaloniaTestApp.Models;

/// <summary>
/// Keyword-based guard used by the SQL Editor to block destructive statements
/// unless the corresponding safety toggle has been explicitly enabled.
/// Checks operate on a single statement at a time — callers are expected to
/// split multi-statement batches first (see DatabaseRepository.SplitStatements).
/// </summary>
public static class SqlSafetyCheck
{
    // \b word-boundary regex — matches DROP/DELETE/TRUNCATE as whole words only,
    // case-insensitive, so it won't false-positive on e.g. a column named "dropdown".
    private static readonly Regex DropRegex = new(@"\bDROP\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DeleteRegex = new(@"\bDELETE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TruncateRegex = new(@"\bTRUNCATE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns (Blocked, Reason) for a single SQL statement. Blocked is true if the
    /// statement contains a guarded keyword whose corresponding toggle is off.
    /// </summary>
    public static (bool Blocked, string Reason) CheckStatement(
        string statement,
        bool allowDrop,
        bool allowDelete,
        bool allowTruncate)
    {
        if (!allowDrop && DropRegex.IsMatch(statement))
            return (true, "Blocked: statement contains DROP and 'Allow DROP' is off.");

        if (!allowDelete && DeleteRegex.IsMatch(statement))
            return (true, "Blocked: statement contains DELETE and 'Allow DELETE' is off.");

        if (!allowTruncate && TruncateRegex.IsMatch(statement))
            return (true, "Blocked: statement contains TRUNCATE and 'Allow TRUNCATE' is off.");

        return (false, string.Empty);
    }
}
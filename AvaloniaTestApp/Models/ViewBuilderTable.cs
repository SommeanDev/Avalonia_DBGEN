using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaTestApp.Models;

/// <summary>
/// Represents one table card placed on the View Builder canvas.
/// X / Y are the card's position on the Canvas (updated on drag).
/// </summary>
public partial class ViewBuilderTable : ObservableObject, ICanvasNode
{
    // ── Identity ────────────────────────────────────────────
    [ObservableProperty]
    private string _tableName = string.Empty;

    /// <summary>Short alias used in the SQL (e.g. "aa1", "aav2").</summary>
    [ObservableProperty]
    private string _alias = string.Empty;

    // ── Canvas position (two-way bound via attached properties) ─
    [ObservableProperty]
    private double _x = 40;

    [ObservableProperty]
    private double _y = 40;

    // ── Columns ─────────────────────────────────────────────
    public ObservableCollection<ViewColumn> Columns { get; } = new();

    // ── Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Generates a compact alias from the table name.
    /// "ah_animal_vaccinations" + index 2  →  "aav2"
    /// </summary>
    public static string BuildAlias(string tableName, int index)
    {
        var parts = tableName.Split('_', System.StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(System.Array.ConvertAll(parts, p => p[0]));
        return initials.ToLowerInvariant() + index;
    }
}

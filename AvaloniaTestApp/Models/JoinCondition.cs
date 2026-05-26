// using System.Collections.Generic;
// using System.Collections.ObjectModel;
// using CommunityToolkit.Mvvm.ComponentModel;
//
// namespace AvaloniaTestApp.Models;
//
// /// <summary>
// /// One JOIN block in the sidebar.
// /// Maps:  JOIN  LeftTable.LeftColumn  =  RightTable.RightColumn
// /// </summary>
// public partial class JoinCondition : ObservableObject
// {
//     // ── Join type ────────────────────────────────────────────
//     public static readonly IReadOnlyList<string> JoinTypes =
//         new[] { "LEFT JOIN", "INNER JOIN", "RIGHT JOIN", "FULL OUTER JOIN", "CROSS JOIN" };
//
//     [ObservableProperty]
//     private string _joinType = "LEFT JOIN";
//
//     // ── Left side (the table being joined) ───────────────────
//     [ObservableProperty]
//     private string _leftTableAlias = string.Empty;
//
//     [ObservableProperty]
//     private string _leftColumn = string.Empty;
//
//     // ── Right side (usually the first / base table) ──────────
//     [ObservableProperty]
//     private string _rightTableAlias = string.Empty;
//
//     [ObservableProperty]
//     private string _rightColumn = string.Empty;
//
//     // ── Derived label shown in sidebar header ────────────────
//     /// <summary>"aav2  →  aa1"</summary>
//     public string Label => $"{_leftTableAlias}  →  {_rightTableAlias}";
//
//     // ── Column lists (populated from the parent tables) ──────
//     /// <summary>Columns available for the left-side picker.</summary>
//     public ObservableCollection<string> LeftColumns { get; } = new();
//
//     /// <summary>Columns available for the right-side picker.</summary>
//     public ObservableCollection<string> RightColumns { get; } = new();
// }

using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaTestApp.Models;

public partial class JoinCondition : ObservableObject
{
    public static readonly IReadOnlyList<string> JoinTypes =
        new[] { "LEFT JOIN", "INNER JOIN", "RIGHT JOIN", "FULL OUTER JOIN", "CROSS JOIN" };

    [ObservableProperty]
    private string _joinType = "LEFT JOIN";

    [ObservableProperty]
    private string _leftTableAlias = string.Empty;

    [ObservableProperty]
    private string _leftColumn = string.Empty;

    [ObservableProperty]
    private string _rightTableAlias = string.Empty;

    [ObservableProperty]
    private string _rightColumn = string.Empty;

    // FIX: use the generated properties (LeftTableAlias / RightTableAlias)
    // not the backing fields (_leftTableAlias / _rightTableAlias)
    public string Label => $"{LeftTableAlias}  →  {RightTableAlias}";

    public ObservableCollection<string> LeftColumns  { get; } = new();
    public ObservableCollection<string> RightColumns { get; } = new();
}
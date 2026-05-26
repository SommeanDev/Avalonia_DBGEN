using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaTestApp.Models;

public partial class WhereCondition : ObservableObject
{
    public static readonly IReadOnlyList<string> LogicalOperators =
        new[] { "AND", "OR" };

    public static readonly IReadOnlyList<string> Operators =
        new[] { "=", "<>", ">", ">=", "<", "<=", "LIKE", "ILIKE", "IN", "IS NULL", "IS NOT NULL" };

    [ObservableProperty]
    private string _logicalOperator = "AND";

    [ObservableProperty]
    private string _selectedColumn = string.Empty;

    [ObservableProperty]
    private string _operator = "=";

    [ObservableProperty]
    private string _value = string.Empty;

    public ObservableCollection<string> AvailableColumns { get; } = new();
}

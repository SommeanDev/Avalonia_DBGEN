using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaTestApp.Models;

public partial class CteNode : ObservableObject, ICanvasNode
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _sourceTableAlias = string.Empty;

    [ObservableProperty]
    private double _x = 80;

    [ObservableProperty]
    private double _y = 420;

    public ObservableCollection<string> AvailableSourceTables { get; } = new();

    public ObservableCollection<ViewColumn> OutputColumns { get; } = new();

    public ObservableCollection<WhereCondition> Conditions { get; } = new();
}

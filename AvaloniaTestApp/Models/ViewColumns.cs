using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaTestApp.Models;

/// <summary>
/// Represents a single column inside a table card on the View Builder canvas.
/// </summary>
public partial class ViewColumn : ObservableObject
{
    [ObservableProperty]
    private string _columnName = string.Empty;

    [ObservableProperty]
    private string _dataType = string.Empty;

    /// <summary>Whether this column appears in the SELECT list.</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Marks the column as a primary key (shows the key icon).</summary>
    [ObservableProperty]
    private bool _isPrimaryKey;

    /// <summary>Marks the column as a foreign key anchor (shows the link icon).</summary>
    [ObservableProperty]
    private bool _isForeignKey;
}
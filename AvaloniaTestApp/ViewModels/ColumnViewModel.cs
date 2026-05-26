using CommunityToolkit.Mvvm.ComponentModel;
using ReactiveUI;

namespace AvaloniaTestApp.ViewModels;

public class ColumnViewModel : ReactiveObject
{
    private string _columnName = ""; 
    public string ColumnName { get => _columnName; set => this.RaiseAndSetIfChanged(ref _columnName, value); }

    private string _dataType = "";
    public string DataType { get => _dataType; set => this.RaiseAndSetIfChanged(ref _dataType, value); }

    private bool _useColumn;
    public bool UseColumn { get => _useColumn; set => this.RaiseAndSetIfChanged(ref _useColumn, value); }

    private bool _isNull;
    public bool IsNull { get => _isNull; set => this.RaiseAndSetIfChanged(ref _isNull, value); }

    private bool _isPK;
    public bool IsPK { get => _isPK; set => this.RaiseAndSetIfChanged(ref _isPK, value); }

    private bool _isID;
    public bool IsID { get => _isID; set => this.RaiseAndSetIfChanged(ref _isID, value); }

    private bool _isArray;
    public bool IsArray { get => _isArray; set => this.RaiseAndSetIfChanged(ref _isArray, value); }

    private string _defaultValue = "";
    public string DefaultValue { get => _defaultValue; set => this.RaiseAndSetIfChanged(ref _defaultValue, value); }
}
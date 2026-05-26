using System;
using Avalonia.Controls;
using AvaloniaTestApp.Models;
using AvaloniaTestApp.ViewModels;

namespace AvaloniaTestApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;
        
        // Opened += (_, _) =>
        // {
        //     vm.Clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        // };
        
        var tableSearch = this.FindControl<AutoCompleteBox>("TableSearchBox");
        if (tableSearch != null)
        {
            tableSearch.ItemFilter = (search, item) =>
                item is DatabaseObject obj &&
                obj.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
        }
    }
}
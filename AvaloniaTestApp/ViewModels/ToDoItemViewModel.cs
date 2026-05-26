// using AvaloniaTestApp.Models;
// using CommunityToolkit.Mvvm.ComponentModel;
//
// namespace AvaloniaTestApp.ViewModels;
//
// public partial class ToDoItemViewModel : ViewModelBase
// {
//     [ObservableProperty]
//     public partial bool isChecked { get; set; }
//     [ObservableProperty]
//     public partial string? content { get; set; }
//     
//     public ToDoItemViewModel() {}
//
//     public ToDoItemViewModel(ToDoItem item)
//     {
//         isChecked = item.IsChecked;
//         content = item.Content;
//     }
//
//     public ToDoItem GetToDoItem()
//     {
//         return new ToDoItem()
//         {
//             IsChecked = this.isChecked,
//             Content = this.content,
//         };
//     }
// }
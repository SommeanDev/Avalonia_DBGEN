using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaTestApp.Models;
using AvaloniaTestApp.ViewModels;

namespace AvaloniaTestApp.Views;

public partial class ViewBuilderView : UserControl
{
    private ICanvasNode? _dragging;
    private Control?     _dragContainer;
    private Point        _dragOffset;

    // Keep a reference so we can subscribe/unsubscribe
    private System.Collections.ObjectModel.ObservableCollection<ViewBuilderTable>? _tables;

    public ViewBuilderView()
    {
        InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
    }

    // ── Wire up to the ViewModel's Tables collection ──────────────────────

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // Unsubscribe from old collection
        if (_tables is not null)
            _tables.CollectionChanged -= OnTablesChanged;

        if (DataContext is ViewBuilderViewModel vm)
        {
            _tables = vm.Tables;
            _tables.CollectionChanged += OnTablesChanged;
        }
    }

    // ── When a table is added, wait for the container to render then position it ─

    private void OnTablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Small delay lets Avalonia generate the ContentPresenter before we set props
        Avalonia.Threading.Dispatcher.UIThread.Post(PositionAllCards,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // ── Position every card by setting Canvas.Left/Top on the ContentPresenter ──

    private void PositionAllCards()
    {
        var ic = this.FindControl<ItemsControl>("TablesItemsControl");
        if (ic is null) return;

        var panel = ic.ItemsPanelRoot as Canvas;
        if (panel is null) return;

        if (DataContext is not ViewBuilderViewModel vm) return;

        // Each child of the Canvas is a ContentPresenter wrapping one table card
        var children = panel.Children;
        for (int i = 0; i < children.Count && i < vm.Tables.Count; i++)
        {
            var container = children[i];
            var table     = vm.Tables[i];

            Canvas.SetLeft(container, table.X);
            Canvas.SetTop(container,  table.Y);
        }
    }

    // ── Canvas drag handlers ──────────────────────────────────────────────

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var canvas = this.FindControl<Canvas>("TableCanvas");
        if (canvas is null) return;

        canvas.AddHandler(PointerPressedEvent,  OnPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Bubble);
        canvas.AddHandler(PointerMovedEvent,    OnPointerMoved,
            Avalonia.Interactivity.RoutingStrategies.Bubble);
        canvas.AddHandler(PointerReleasedEvent, OnPointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Bubble);

        // Also subscribe to Tables if DataContext was set before Loaded
        OnDataContextChanged(null, System.EventArgs.Empty);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (IsInteractiveChild(e.Source as Visual)) return;

        var (container, table) = FindCardContainer(e.Source as Visual);
        if (container is null || table is null) return;

        _dragging      = table;
        _dragContainer = container;

        var canvas  = this.FindControl<Canvas>("TableCanvas")!;
        var pos     = e.GetPosition(canvas);
        _dragOffset = new Point(pos.X - table.X, pos.Y - table.Y);

        e.Pointer.Capture(container);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null) return;

        var canvas = this.FindControl<Canvas>("TableCanvas")!;
        var pos    = e.GetPosition(canvas);

        _dragging.X = System.Math.Max(0, pos.X - _dragOffset.X);
        _dragging.Y = System.Math.Max(0, pos.Y - _dragOffset.Y);

        // Directly update the Canvas attached properties too so it moves immediately
        if (_dragContainer is not null)
        {
            Canvas.SetLeft(_dragContainer, _dragging.X);
            Canvas.SetTop(_dragContainer,  _dragging.Y);
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        _dragging      = null;
        _dragContainer = null;
        e.Handled = true;
    }

    // ── Walk visual tree: Border (Tag=VM) → parent ContentPresenter ───────

    private static (Control? container, ICanvasNode? node) FindCardContainer(Visual? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Border b && b.Tag is ICanvasNode node)
            {
                var container = b.GetVisualParent() as Control;
                return (container, node);
            }
            current = current.GetVisualParent();
        }
        return (null, null);
    }

    private static bool IsInteractiveChild(Visual? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Border { Tag: ICanvasNode })
                return false;

            if (current is Button or CheckBox or ComboBox or TextBox or ScrollViewer)
                return true;

            current = current.GetVisualParent();
        }

        return false;
    }
}

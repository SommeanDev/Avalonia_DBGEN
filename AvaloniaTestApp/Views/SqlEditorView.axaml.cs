using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaTestApp.ViewModels;

namespace AvaloniaTestApp.Views;

public partial class SqlEditorView : UserControl
{
    private SqlEditorViewModel? _viewModel;
    private bool _suppressEditorTextChanged;
    private bool _highlightingApplied;
    private CompletionWindow? _completionWindow;

    // Keyword tier — same vocabulary as the highlighting definition below, kept as a
    // single source of truth via SqlKeywords so the two never drift apart.
    private static readonly string[] SqlKeywords =
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET",
        "DELETE", "DROP", "TRUNCATE", "CREATE", "TABLE", "ALTER", "JOIN", "LEFT",
        "RIGHT", "INNER", "OUTER", "ON", "AND", "OR", "NOT", "NULL",
        "IS", "IN", "AS", "ORDER", "BY", "GROUP", "HAVING", "LIMIT",
        "DISTINCT", "UNION", "ALL", "CASE", "WHEN", "THEN", "ELSE", "END",
        "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "FUNCTION", "PROCEDURE",
        "RETURNS", "LANGUAGE", "DECLARE", "EXISTS", "DEFAULT", "PRIMARY",
        "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT"
    };

    public SqlEditorView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => AttachViewModel();
        AttachedToVisualTree += (_, _) =>
        {
            EnsureHighlightingApplied();
            EnsureCompletionWired();
        };
    }

    // ----- Autocomplete (keyword tier) -----

    private bool _completionWired;

    private void EnsureCompletionWired()
    {
        if (_completionWired) return;

        var editor = this.FindControl<AvaloniaEdit.TextEditor>("SqlTextEditor");
        if (editor == null) return;

        // Reuses the same TextChanged event already wired for ViewModel sync (see
        // EnsureHighlightingApplied) rather than TextArea.TextEntered, since that event's
        // exact signature varies across AvaloniaEdit/Avalonia versions — TextChanged is
        // the one connection point already confirmed to compile against this project.
        editor.TextChanged += OnEditorTextChangedForCompletion;
        editor.KeyDown += OnEditorKeyDown;
        _completionWired = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space forces the completion popup open regardless of how many characters
        // have been typed — the standard "show me everything" gesture.
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            var editor = this.FindControl<AvaloniaEdit.TextEditor>("SqlTextEditor");
            if (editor != null)
            {
                ShowCompletion(editor.TextArea, requireMinLength: false);
                e.Handled = true;
            }
        }
    }

    private void OnEditorTextChangedForCompletion(object? sender, EventArgs e)
    {
        var editor = this.FindControl<AvaloniaEdit.TextEditor>("SqlTextEditor");
        if (editor == null) return;

        var currentWord = GetCurrentWord(editor.TextArea);

        // Empty word (just typed a space/operator/etc.) — close any open popup and stop.
        if (currentWord.Length == 0)
        {
            _completionWindow?.Close();
            return;
        }

        // Type-ahead trigger: once at least 2 word characters of the current token have
        // been typed, offer matching suggestions automatically (closer to the Monaco
        // "as you type" feel than requiring Ctrl+Space every time).
        if (currentWord.Length >= 2)
        {
            ShowCompletion(editor.TextArea, requireMinLength: true);
        }
        else
        {
            _completionWindow?.Close();
        }
    }

    /// <summary>
    /// Walks backwards from the caret to find the word currently being typed, so we can
    /// both decide whether to trigger completion and filter/replace the right span.
    /// </summary>
    private static string GetCurrentWord(TextArea textArea)
    {
        var doc = textArea.Document;
        int caret = textArea.Caret.Offset;
        int start = caret;
        while (start > 0 && (char.IsLetterOrDigit(doc.GetCharAt(start - 1)) || doc.GetCharAt(start - 1) == '_'))
            start--;
        return doc.GetText(start, caret - start);
    }

    private void ShowCompletion(TextArea textArea, bool requireMinLength)
    {
        var currentWord = GetCurrentWord(textArea);
        if (requireMinLength && currentWord.Length < 2) return;

        var matches = SqlKeywords
            .Where(k => k.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k)
            .ToList();

        if (matches.Count == 0)
        {
            _completionWindow?.Close();
            return;
        }

        // Don't reopen a window that's already showing — just refresh its data and its
        // EndOffset. AvaloniaEdit does NOT auto-track the end of the replacement span as
        // more characters are typed (StartOffset/EndOffset are set once at construction),
        // so failing to update EndOffset here would replace a stale, too-short span when
        // the suggestion is eventually accepted.
        if (_completionWindow != null)
        {
            _completionWindow.EndOffset = textArea.Caret.Offset;
            _completionWindow.CompletionList.CompletionData.Clear();
            foreach (var match in matches)
                _completionWindow.CompletionList.CompletionData.Add(new SqlCompletionData(match));
            return;
        }

        _completionWindow = new CompletionWindow(textArea)
        {
            StartOffset = textArea.Caret.Offset - currentWord.Length,
            EndOffset = textArea.Caret.Offset
        };

        _completionWindow.CompletionList.Foreground = Brushes.White;
        
        foreach (var match in matches)
            _completionWindow.CompletionList.CompletionData.Add(new SqlCompletionData(match));

        _completionWindow.Show();
        _completionWindow.Closed += (_, _) => _completionWindow = null;
    }

    /// <summary>
    /// A single suggestion entry. Keyword tier just inserts the keyword text; the planned
    /// schema-aware tier can subclass or add a second ICompletionData implementation for
    /// table/column entries without touching this one.
    /// </summary>
    private class SqlCompletionData : ICompletionData
    {
        public SqlCompletionData(string text) => Text = text;

        public IImage? Image => null;
        public string Text { get; }
    
        // ─── CUSTOM CONTROL WITH EXPLICIT STYLING ────────────────────────
        // Returning a TextBlock tells Avalonia exactly how to render the row,
        // overriding theme brush mismatches.
        public object Content => new TextBlock 
        { 
            Text = this.Text, 
            Foreground = Brushes.LightGray, 
            FontFamily = new FontFamily("Cascadia Code,Consolas,Monospace"),
            FontSize = 13,
            Padding = new Thickness(4, 2)
        };
        // ───────────────────────────────────────────────────────────────────
    
        public object Description => $"SQL keyword";
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    /// <summary>
    /// Wires the editor's TextChanged handler and applies syntax highlighting. Run from
    /// AttachedToVisualTree rather than the constructor: named controls resolved via
    /// FindControl can come back null if called before the control template has been
    /// fully applied, which previously caused highlighting to silently never get set.
    /// </summary>
    private void EnsureHighlightingApplied()
    {
        if (_highlightingApplied) return;

        var editor = this.FindControl<AvaloniaEdit.TextEditor>("SqlTextEditor");
        if (editor == null)
        {
            Debug.WriteLine("[SqlEditorView] SqlTextEditor control not found — cannot apply highlighting.");
            return;
        }

        editor.TextChanged += OnEditorTextChanged;

        try
        {
            editor.SyntaxHighlighting = BuildSqlHighlighting();
            _highlightingApplied = true;
        }
        catch (Exception ex)
        {
            // Surface the failure loudly instead of leaving the editor silently uncolored.
            Debug.WriteLine($"[SqlEditorView] Failed to build SQL highlighting: {ex}");
            if (_viewModel != null)
                _viewModel.StatusMessage = $"Syntax highlighting failed to load: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds a minimal SQL keyword highlighting definition in-memory via XSHD, rather than
    /// relying on AvaloniaEdit shipping a built-in "SQL" definition (it may not, depending on
    /// version). This guarantees consistent keyword coloring regardless of what's bundled.
    /// </summary>
    private static IHighlightingDefinition BuildSqlHighlighting()
    {
        const string xshd = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="SQL" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Keyword" foreground="#569CD6" exampleText="SELECT * FROM table"/>
          <Color name="Comment" foreground="#6A9955" exampleText="-- comment"/>
          <Color name="String" foreground="#CE9178" exampleText="'value'"/>
          <Color name="Number" foreground="#B5CEA8" exampleText="123"/>
          <RuleSet>
            <Span color="Comment" begin="--"/>
            <Span color="Comment" multiline="true" begin="/\*" end="\*/"/>
            <Span color="String">
              <Begin>'</Begin>
              <End>'</End>
            </Span>
            <Keywords color="Keyword">
              <Word>SELECT</Word><Word>FROM</Word><Word>WHERE</Word><Word>INSERT</Word>
              <Word>INTO</Word><Word>VALUES</Word><Word>UPDATE</Word><Word>SET</Word>
              <Word>DELETE</Word><Word>DROP</Word><Word>TRUNCATE</Word><Word>CREATE</Word>
              <Word>TABLE</Word><Word>ALTER</Word><Word>JOIN</Word><Word>LEFT</Word>
              <Word>RIGHT</Word><Word>INNER</Word><Word>OUTER</Word><Word>ON</Word>
              <Word>AND</Word><Word>OR</Word><Word>NOT</Word><Word>NULL</Word>
              <Word>IS</Word><Word>IN</Word><Word>AS</Word><Word>ORDER</Word>
              <Word>BY</Word><Word>GROUP</Word><Word>HAVING</Word><Word>LIMIT</Word>
              <Word>DISTINCT</Word><Word>UNION</Word><Word>ALL</Word><Word>CASE</Word>
              <Word>WHEN</Word><Word>THEN</Word><Word>ELSE</Word><Word>END</Word>
              <Word>BEGIN</Word><Word>COMMIT</Word><Word>ROLLBACK</Word><Word>TRANSACTION</Word>
              <Word>FUNCTION</Word><Word>PROCEDURE</Word><Word>RETURNS</Word><Word>LANGUAGE</Word>
              <Word>DECLARE</Word><Word>EXISTS</Word><Word>DEFAULT</Word><Word>PRIMARY</Word>
              <Word>KEY</Word><Word>FOREIGN</Word><Word>REFERENCES</Word><Word>CONSTRAINT</Word>
            </Keywords>
            <Rule color="Number">\b\d+(\.\d+)?\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

        using var reader = XmlReader.Create(new System.IO.StringReader(xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void AttachViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ResultColumns.CollectionChanged -= OnResultsChanged;
            _viewModel.ResultRows.CollectionChanged -= OnResultsChanged;
        }

        _viewModel = DataContext as SqlEditorViewModel;

        if (_viewModel == null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ResultColumns.CollectionChanged += OnResultsChanged;
        _viewModel.ResultRows.CollectionChanged += OnResultsChanged;

        // Sync the editor's initial text (e.g. if SqlQuery was pre-populated)
        SyncEditorTextFromViewModel();
        RebuildResultsDisplay();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SqlEditorViewModel.SqlQuery))
        {
            SyncEditorTextFromViewModel();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorTextChanged || _viewModel == null) return;

        var editor = this.FindControl<AvaloniaEdit.TextEditor>("SqlTextEditor");
        if (editor == null) return;

        _viewModel.SqlQuery = editor.Text;
    }

    private void SyncEditorTextFromViewModel()
    {
        if (_viewModel == null) return;

        var editor = this.FindControl<AvaloniaEdit.TextEditor>("SqlTextEditor");
        if (editor == null) return;

        // Avoid feedback loop: don't let the change we're about to make re-trigger
        // OnEditorTextChanged and write the same text straight back to the ViewModel.
        if (editor.Text == _viewModel.SqlQuery) return;

        _suppressEditorTextChanged = true;
        try
        {
            editor.Text = _viewModel.SqlQuery ?? string.Empty;
        }
        finally
        {
            _suppressEditorTextChanged = false;
        }
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildResultsDisplay();
    }

    /// <summary>
    /// Rebuilds the DataGrid's columns from the current ResultColumns/ResultRows, since the
    /// shape of the result set is unknown until a query actually runs. If there are no
    /// columns to show (e.g. a DML-only batch, or every statement was blocked), the grid is
    /// hidden in favor of the per-statement breakdown panel instead.
    /// </summary>
    private void RebuildResultsDisplay()
    {
        if (_viewModel == null) return;

        var grid = this.FindControl<DataGrid>("ResultGrid");
        var breakdownPanel = this.FindControl<ScrollViewer>("StatementBreakdownPanel");
        if (grid == null || breakdownPanel == null) return;

        var columns = _viewModel.ResultColumns;
        var rows = _viewModel.ResultRows;

        if (columns.Count == 0)
        {
            grid.IsVisible = false;
            breakdownPanel.IsVisible = true;
            grid.Columns.Clear();
            grid.ItemsSource = null;
            return;
        }

        grid.IsVisible = true;
        breakdownPanel.IsVisible = false;

        grid.Columns.Clear();
        for (int i = 0; i < columns.Count; i++)
        {
            int colIndex = i; // capture for the converter below
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding
                {
                    Converter = new RowCellConverter(colIndex)
                }
            });
        }

        grid.ItemsSource = rows;
    }

    /// <summary>
    /// Each row in ResultRows is an object[]; this converter extracts the cell at a fixed
    /// column index so DataGridTextColumn.Binding can read it without needing an indexer
    /// path expression (object[] doesn't support "[i]" binding paths the way a List does).
    /// </summary>
    private class RowCellConverter : IValueConverter
    {
        private readonly int _index;
        public RowCellConverter(int index) => _index = index;

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is object[] row && _index < row.Length)
                return row[_index]?.ToString() ?? "NULL";
            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
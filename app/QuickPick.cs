using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Templates;
using Avalonia.Threading;

namespace LuminaIDE;

/// <summary>One row in the quick pick. <see cref="Tag"/> carries whatever the caller needs back.</summary>
public sealed record PickItem(string Title, string? Detail = null, string? Hint = null, object? Tag = null, string? IconFile = null);

/// <summary>Fuzzy subsequence matching: consecutive letters and word starts score higher.</summary>
public static class Fuzzy
{
    public static int Score(string text, string query)
    {
        if (query.Length == 0) return 0;
        string t = text.ToLowerInvariant(), q = query.ToLowerInvariant();
        int ti = 0, score = 0, last = -2;
        foreach (var qc in q)
        {
            int idx = t.IndexOf(qc, ti);
            if (idx < 0) return -1;
            score += 10;
            if (idx == last + 1) score += 15;
            if (idx == 0 || "/\\-_. ".Contains(t[idx - 1])) score += 12;
            last = idx;
            ti = idx + 1;
        }
        return score - text.Length / 6; // prefer shorter candidates
    }

    /// <summary>Best score of the title alone (weighted up) and title + detail together.</summary>
    public static int Score(PickItem item, string query)
    {
        if (query.Length == 0) return 0;
        int a = Score(item.Title, query);
        int b = item.Detail is null ? -1 : Score(item.Detail + "/" + item.Title, query);
        return a >= 0 ? a + 20 : b;
    }
}

/// <summary>
/// A centered, keyboard-driven picker floating over the window: type to filter, ↑/↓ to move,
/// Enter to choose, Esc to cancel. Used for quick open, the command palette and pickers.
/// </summary>
public sealed class QuickPick : UserControl
{
    readonly TextBox _input = new() { Padding = new Thickness(12, 10), FontSize = 14 };
    readonly ListBox _list = new() { MaxHeight = 380, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _empty = new() { Text = "Nothing matches.", Margin = new Thickness(14, 10), IsVisible = false };
    IReadOnlyList<PickItem> _all = [];
    Action<PickItem>? _onPick;
    bool _picked;
    readonly Border _card;

    /// <summary>Fires while the selection moves (used to preview themes live).</summary>
    public event Action<PickItem?>? Highlighted;
    /// <summary>Fires when the picker closes; the argument is true when an item was chosen.</summary>
    public event Action<bool>? Closed;

    public QuickPick()
    {
        IsVisible = false;
        _empty.Classes.Add("muted");
        _list.ItemTemplate = new FuncDataTemplate<PickItem>((item, _) => Row(item), supportsRecycling: false);

        _card = new Border
        {
            Width = 640, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 70, 0, 0), Padding = new Thickness(8),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { _input, _list, _empty } },
        };
        _card.Classes.Add("floating");
        _card.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) },
            new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(160) },
        };

        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) };
        scrim.PointerPressed += (_, _) => Close(false);
        Content = new Grid { Children = { scrim, _card } };

        _input.TextChanged += (_, _) => Refilter();
        _input.AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.DoubleTapped += (_, _) => Choose();
        _list.Tapped += (_, _) => { if (_list.SelectedItem is not null) Choose(); };
        _list.SelectionChanged += (_, _) => Highlighted?.Invoke(_list.SelectedItem as PickItem);
    }

    static Control Row(PickItem item)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(4, 2) };
        if (item.IconFile is not null) grid.Children.Add(new FileIcon { FileName = item.IconFile, Size = 16, Margin = new Thickness(0, 0, 10, 0) });
        var left = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(left, 1);
        left.Children.Add(new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(item.Detail))
        {
            var d = new TextBlock { Text = item.Detail, FontSize = 11, TextTrimming = TextTrimming.PrefixCharacterEllipsis };
            d.Classes.Add("muted");
            left.Children.Add(d);
        }
        grid.Children.Add(left);
        if (!string.IsNullOrEmpty(item.Hint))
        {
            var h = new TextBlock { Text = item.Hint, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
            h.Classes.Add("muted");
            Grid.SetColumn(h, 2);
            grid.Children.Add(h);
        }
        return grid;
    }

    public void Show(string placeholder, IReadOnlyList<PickItem> items, Action<PickItem> onPick, string initialQuery = "")
    {
        _all = items;
        _onPick = onPick;
        _picked = false;
        _input.Watermark = placeholder;
        _input.Text = initialQuery;
        _card.Opacity = 0;
        _card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(-8px) scale(0.985)");
        IsVisible = true;
        Refilter();
        Dispatcher.UIThread.Post(() => { _card.Opacity = 1; _card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px) scale(1)"); }, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => { _input.Focus(); _input.CaretIndex = _input.Text?.Length ?? 0; }, DispatcherPriority.Input);
    }

    public bool IsOpen => IsVisible;

    public void Close(bool picked)
    {
        if (!IsVisible) return;
        IsVisible = false;
        Closed?.Invoke(picked);
    }

    void Refilter()
    {
        var q = (_input.Text ?? "").Trim();
        var shown = q.Length == 0
            ? _all.Take(80).ToList()
            : _all.Select(i => (Item: i, Score: Fuzzy.Score(i, q))).Where(x => x.Score >= 0).OrderByDescending(x => x.Score).Take(80).Select(x => x.Item).ToList();
        _list.ItemsSource = shown;
        _list.SelectedIndex = shown.Count > 0 ? 0 : -1;
        _empty.IsVisible = shown.Count == 0;
        _list.IsVisible = shown.Count > 0;
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.PageDown: Move(8); e.Handled = true; break;
            case Key.PageUp: Move(-8); e.Handled = true; break;
            case Key.Enter: Choose(); e.Handled = true; break;
            case Key.Escape: Close(false); e.Handled = true; break;
        }
    }

    void Move(int delta)
    {
        int count = (_list.ItemsSource as IReadOnlyList<PickItem>)?.Count ?? 0;
        if (count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    void Choose()
    {
        if (_list.SelectedItem is not PickItem item) return;
        _picked = true;
        var pick = _onPick;
        Close(true);
        pick?.Invoke(item);
    }

    public bool WasPicked => _picked;
}

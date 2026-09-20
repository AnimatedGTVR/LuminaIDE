using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LuminaIDE;

/// <summary>Small builders for the controls used on the settings and web pages. All styling lives in App.axaml.</summary>
static class Form
{
    public static TextBlock Section(string title)
    {
        var t = new TextBlock { Text = title.ToUpperInvariant(), Margin = new Thickness(0, 22, 0, 4) };
        t.Classes.Add("section");
        return t;
    }

    /// <summary>A titled group of settings: small caps title above a rounded card whose rows are split by hairlines.</summary>
    public sealed class Card
    {
        readonly StackPanel _rows = new();
        public Control Title { get; }
        public Border Body { get; }

        public Card(string title)
        {
            var t = new TextBlock { Text = title.ToUpperInvariant(), Margin = new Thickness(4, 26, 0, 8) };
            t.Classes.Add("section");
            Title = t;
            Body = new Border { Child = _rows, Padding = new Thickness(18, 4) };
            Body.Classes.Add("card");
        }

        public Card Add(Control row)
        {
            if (_rows.Children.Count > 0)
            {
                var line = new Border();
                line.Classes.Add("divider");
                _rows.Children.Add(line);
            }
            _rows.Children.Add(row);
            return this;
        }

        public void AddTo(Panel page) { page.Children.Add(Title); page.Children.Add(Body); }
    }

    public static Border Chip(string text, bool active, Action tap)
    {
        var chip = new Border { Child = new TextBlock { Text = text, FontSize = 12 }, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(11, 5) };
        chip.Classes.Add("chip");
        chip.Classes.Set("active", active);
        chip.Tapped += (_, _) => tap();
        return chip;
    }

    public static Border Button(string text, Action tap, bool primary = false)
    {
        var b = new Border { Child = new TextBlock { Text = text, FontSize = 12.5 }, Margin = new Thickness(0, 0, 8, 8) };
        b.Classes.Add(primary ? "btn" : "outline");
        if (primary) b.Padding = new Thickness(14, 7);
        b.Tapped += (_, _) => tap();
        return b;
    }

    public static Border Toggle(bool on, Action<bool> changed)
    {
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Child = knob, HorizontalAlignment = HorizontalAlignment.Right };
        track.Classes.Add("toggle");
        void Paint() { track.Classes.Set("on", on); knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; }
        Paint();
        track.Tapped += (_, _) => { on = !on; Paint(); changed(on); };
        return track;
    }

    public static Control Stepper(double value, double min, double max, double step, Action<double> changed, string format = "0.#")
    {
        var label = new TextBlock { Text = value.ToString(format), MinWidth = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Border Btn(string text, int dir)
        {
            var b = new Border { Child = new TextBlock { Text = text, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center }, Width = 28, Height = 26 };
            b.Classes.Add("item");
            b.Tapped += (_, _) =>
            {
                value = Math.Clamp(Math.Round((value + dir * step) * 100) / 100, min, max);
                label.Text = value.ToString(format);
                changed(value);
            };
            return b;
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(Btn("−", -1));
        panel.Children.Add(label);
        panel.Children.Add(Btn("+", +1));
        return panel;
    }

    /// <summary>Label (and optional hint) on the left, the control on the right.</summary>
    public static Control Row(string label, string? hint, Control control)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label });
        if (hint is not null)
        {
            var h = new TextBlock { Text = hint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            h.Classes.Add("muted");
            text.Children.Add(h);
        }
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 12) };
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        var row = new Border { Child = grid, BorderBrush = null };
        row.Classes.Add("settings-row");
        return row;
    }

    /// <summary>Renders a row of chips for a set of options; picking one calls <paramref name="changed"/>.</summary>
    public static Control ChipRow(IEnumerable<(string Label, string Value)> options, string current, Action<string> changed)
    {
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 420 };
        foreach (var (label, value) in options)
            wrap.Children.Add(Chip(label, value == current, () => changed(value)));
        return wrap;
    }
}

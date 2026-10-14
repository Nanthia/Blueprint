using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Blueprint;

public sealed class ResizeDialog : BlueprintWindow
{
    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }

    readonly int ow, oh;
    readonly TextBox tw = new() { MinHeight = 40, Padding = new Thickness(12, 0, 34, 0), FontSize = 15 };
    readonly TextBox th = new() { MinHeight = 40, Padding = new Thickness(12, 0, 34, 0), FontSize = 15 };
    readonly ToggleButton link = new() { Style = (Style)Application.Current.FindResource("IconToggle"), Width = 40, Height = 40, IsChecked = true, ToolTip = "Keep aspect ratio" };
    readonly CheckBox keep = new() { Content = "Keep aspect ratio", IsChecked = true };
    readonly List<(ToggleButton btn, int pct)> presets = new();
    bool updating;

    public ResizeDialog(Window owner, int w, int h)
    {
        Owner = owner;
        ow = w; oh = h;
        NewWidth = w; NewHeight = h;
        Title = "Resize image";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowMinimize = false; ShowMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        tw.Text = w.ToString(); th.Text = h.ToString();
        tw.TextChanged += (_, _) => Linked(true);
        th.TextChanged += (_, _) => Linked(false);
        link.Content = Ui.Icon("Link", 18);
        link.Click += (_, _) => keep.IsChecked = link.IsChecked;
        keep.Click += (_, _) => link.IsChecked = keep.IsChecked;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        FrameworkElement Field(string label, TextBox box, int col)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, Foreground = Ui.B("Text2Brush"), Margin = new Thickness(0, 0, 0, 8) });
            var g = new Grid();
            g.Children.Add(box);
            g.Children.Add(new TextBlock { Text = "px", Foreground = Ui.B("Text3Brush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false });
            sp.Children.Add(g);
            Grid.SetColumn(sp, col);
            return sp;
        }
        grid.Children.Add(Field("Width", tw, 0));
        grid.Children.Add(Field("Height", th, 2));
        link.VerticalAlignment = VerticalAlignment.Bottom;
        link.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(link, 1);
        grid.Children.Add(link);

        var presetRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var pct in new[] { 25, 50, 75, 100, 200 })
        {
            var b = new ToggleButton { Style = (Style)Application.Current.FindResource("PillToggle"), Content = pct + "%", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
            var pp = pct;
            b.Click += (_, _) => SetSize(Math.Max(1, ow * pp / 100), Math.Max(1, oh * pp / 100));
            presets.Add((b, pct));
            presetRow.Children.Add(b);
        }

        var warn = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xD6, 0x0A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD6, 0x0A)),
            BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusMd"), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 18, 0, 0),
        };
        var wr = new Grid();
        wr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        wr.ColumnDefinitions.Add(new ColumnDefinition());
        var wi = Ui.Icon("Info", 18, new SolidColorBrush(Color.FromRgb(0xE8, 0xD9, 0x8A)));
        wi.VerticalAlignment = VerticalAlignment.Top; wi.Margin = new Thickness(0, 1, 12, 0);
        wr.Children.Add(wi);
        var wt = new TextBlock { Text = "Annotations will be merged into the image and can no longer be edited.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xD9, 0x8A)) };
        Grid.SetColumn(wt, 1);
        wr.Children.Add(wt);
        warn.Child = wr;

        var ok = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Content = "Resize", MinWidth = 96, Height = 36, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 96, Height = 36, IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(tw.Text, out var nw) || !int.TryParse(th.Text, out var nh) || nw < 1 || nh < 1 || nw > 16000 || nh > 16000)
            { warnText("Enter a width and height between 1 and 16000."); return; }
            NewWidth = nw; NewHeight = nh;
            DialogResult = true;
        };
        void warnText(string t) { wt.Text = t; }

        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        body.Children.Add(grid);
        keep.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(keep);
        body.Children.Add(new TextBlock { Text = "Presets", Foreground = Ui.B("Text2Brush"), Margin = new Thickness(0, 18, 0, 8) });
        body.Children.Add(presetRow);
        body.Children.Add(warn);

        var footer = new Border { Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 14, 24, 14) };
        var fr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        fr.Children.Add(cancel); fr.Children.Add(ok);
        footer.Child = fr;

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        UpdatePresets();
        Loaded += (_, _) => { tw.Focus(); tw.SelectAll(); };
    }

    void SetSize(int w, int h)
    {
        updating = true;
        tw.Text = w.ToString(); th.Text = h.ToString();
        updating = false;
        UpdatePresets();
    }

    void UpdatePresets()
    {
        if (!int.TryParse(tw.Text, out var w)) w = -1;
        foreach (var (btn, pct) in presets) btn.IsChecked = w == Math.Max(1, ow * pct / 100);
    }

    void Linked(bool fromWidth)
    {
        if (updating) { return; }
        if (keep.IsChecked == true)
        {
            updating = true;
            if (fromWidth && int.TryParse(tw.Text, out var w) && w > 0)
                th.Text = Math.Max(1, (int)Math.Round(w * (double)oh / ow)).ToString(CultureInfo.InvariantCulture);
            else if (!fromWidth && int.TryParse(th.Text, out var h) && h > 0)
                tw.Text = Math.Max(1, (int)Math.Round(h * (double)ow / oh)).ToString(CultureInfo.InvariantCulture);
            updating = false;
        }
        UpdatePresets();
    }
}

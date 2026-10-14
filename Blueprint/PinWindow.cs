using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Blueprint;

/// <summary>Floating always-on-top image (the original's PinWindowController).</summary>
public sealed class PinWindow : Window
{
    readonly BitmapSource img;
    double scale = 1;     // image pixels -> physical pixels
    double dpiScale = Native.SystemScale;
    readonly Image view = new() { Stretch = Stretch.Fill };
    readonly Border badge = new() { Background = new SolidColorBrush(Color.FromArgb(235, 36, 36, 40)), BorderBrush = Ui.B("Line3Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 12, 0, 0), Opacity = 0, IsHitTestVisible = false };
    readonly TextBlock badgeText = new() { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold };
    readonly DispatcherTimer badgeTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly Popup menu = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.MousePoint };
    readonly System.Collections.Generic.List<RadioButton> opacityButtons = new();

    public PinWindow(BitmapSource image)
    {
        img = image;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        Title = "Pin";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Ui.B("TextBrush");

        view.Source = img;
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
        var grid = new Grid();
        grid.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = view });
        badgeText.Text = "100%";
        badge.Child = badgeText;
        grid.Children.Add(badge);
        Content = new Border
        {
            Child = grid, Margin = new Thickness(18),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 36, ShadowDepth = 14, Direction = 270, Opacity = 0.55 },
        };
        Resize(keepCenter: false);

        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Close(); else DragMove(); };
        MouseRightButtonUp += (_, _) => OpenMenu();
        MouseWheel += OnWheel;
        KeyDown += (_, e) =>
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Escape) Close();
            else if (ctrl && e.Key == Key.C) Output.CopyToClipboard(img);
            else if (ctrl && e.Key == Key.S) SaveIt();
        };
        badgeTimer.Tick += (_, _) => { badgeTimer.Stop(); badge.Opacity = 0; };
        BuildMenu();
        Loaded += (_, _) => { dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX; Resize(true); Activate(); };
        Closed += (_, _) => menu.IsOpen = false;
    }

    void SaveIt()
    {
        var p = Output.SaveToFolder(img, out var err);
        ToastWindow.Show(p != null ? "Saved to " + new System.IO.DirectoryInfo(Settings.Current.Folder).Name : "Can't save the image", p != null ? System.IO.Path.GetFileName(p) : err, null, p != null);
    }

    // ---------- menu ----------

    void BuildMenu()
    {
        var stack = new StackPanel { Width = 260 };
        FrameworkElement Row(string icon, string label, string keys, Action a, bool highlight = false)
        {
            var g = new Grid { Height = 40, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Ui.Icon(icon, 18, Ui.B("Text2Brush")); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 }; Grid.SetColumn(t, 1); g.Children.Add(t);
            if (keys.Length > 0) { var k = new TextBlock { Text = keys, Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) }; Grid.SetColumn(k, 2); g.Children.Add(k); }
            var host = new Border { CornerRadius = Ui.Radius("RadiusMd"), Child = g, Background = highlight ? Ui.B("Bg4Brush") : Brushes.Transparent };
            host.MouseEnter += (_, _) => host.Background = Ui.B("Bg4Brush");
            host.MouseLeave += (_, _) => host.Background = highlight ? Ui.B("Bg4Brush") : Brushes.Transparent;
            host.MouseLeftButtonUp += (_, _) => { menu.IsOpen = false; a(); };
            return host;
        }
        stack.Children.Add(Row("Copy", "Copy", "Ctrl C", () => Output.CopyToClipboard(img)));
        stack.Children.Add(Row("Edit", "Edit", "", () => { var ed = new EditorWindow(img); ed.Show(); ed.Activate(); }));
        stack.Children.Add(Row("Save", "Save…", "Ctrl S", SaveIt));
        stack.Children.Add(new Border { Height = 1, Background = Ui.B("Line2Brush"), Margin = new Thickness(6, 6, 6, 6) });

        stack.Children.Add(new TextBlock { Text = "Opacity", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(12, 4, 0, 8), FontSize = 13 });
        var seg = new UniformGrid { Columns = 4 };
        foreach (var o in new[] { 100, 75, 50, 25 })
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegmentItem"), GroupName = "pinopacity", Content = o + "%", IsChecked = o == 100, Padding = new Thickness(0, 7, 0, 7) };
            var oo = o;
            rb.Checked += (_, _) => Opacity = oo / 100.0;
            opacityButtons.Add(rb);
            seg.Children.Add(rb);
        }
        stack.Children.Add(new Border { Style = (Style)FindResource("SegmentBox"), Child = seg, Margin = new Thickness(6, 0, 6, 6) });
        stack.Children.Add(new Border { Height = 1, Background = Ui.B("Line2Brush"), Margin = new Thickness(6, 4, 6, 6) });
        stack.Children.Add(Row("Close", "Close", "Esc", Close, highlight: true));

        menu.Child = new Border
        {
            Background = Ui.B("MenuBrush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusLg"),
            Padding = new Thickness(8), Margin = new Thickness(0, 0, 28, 28), Child = stack, Effect = (System.Windows.Media.Effects.Effect)FindResource("PopoverShadow"),
        };
        menu.PlacementTarget = this;
    }

    void OpenMenu() => menu.IsOpen = true;

    // ---------- sizing ----------

    void Resize(bool keepCenter)
    {
        double cx = Left + ActualWidth / 2, cy = Top + ActualHeight / 2;
        view.Width = img.PixelWidth * scale / dpiScale;
        view.Height = img.PixelHeight * scale / dpiScale;
        SizeToContent = SizeToContent.WidthAndHeight;
        if (keepCenter)
        {
            UpdateLayout();
            Left = cx - ActualWidth / 2;
            Top = cy - ActualHeight / 2;
        }
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        scale = Math.Clamp(scale * Math.Pow(1.1, e.Delta / 120.0), 0.1, 8);
        Resize(true);
        badgeText.Text = $"{scale * 100:0}%";
        badge.Opacity = 1;
        badgeTimer.Stop(); badgeTimer.Start();
    }
}

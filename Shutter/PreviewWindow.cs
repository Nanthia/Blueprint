using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Shutter;

/// <summary>Small corner card shown after a capture instead of the full editor.</summary>
public sealed class PreviewWindow : Window
{
    readonly BitmapSource img;
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Border progress = new() { Height = 2, Background = Ui.B("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1) };
    readonly double total = 7000;
    double elapsed;
    bool hovered, closing;
    double barWidth = 300;

    public PreviewWindow(BitmapSource image)
    {
        img = image;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;
        Title = "Shutter preview";
        Opacity = 0;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = Ui.B("TextBrush");

        // thumbnail with hover affordance
        var view = new Image { Source = img, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 380, MaxHeight = 240 };
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.HighQuality);
        var openPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 28, 28, 32)), BorderBrush = Ui.B("Line3Brush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0, IsHitTestVisible = false, Child = Ui.IconLabel("Edit", "Open in editor", 18, 10),
        };
        var thumbGrid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        thumbGrid.Children.Add(new Border { Child = view, CornerRadius = new CornerRadius(10), ClipToBounds = true });
        thumbGrid.Children.Add(openPill);
        var thumb = new Border
        {
            CornerRadius = new CornerRadius(10), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), Background = Ui.B("Bg0Brush"),
            ClipToBounds = true, Child = thumbGrid,
        };
        thumbGrid.MouseEnter += (_, _) => openPill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        thumbGrid.MouseLeave += (_, _) => openPill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        thumbGrid.MouseLeftButtonUp += (_, _) => OpenEditor();

        // actions
        var copy = new Button { Style = (Style)FindResource("PrimaryButton"), Content = Ui.IconLabel("Copy", "Copy", 16, 8), Height = 38, Padding = new Thickness(16, 0, 18, 0), ToolTip = "Copy to clipboard" };
        copy.Click += (_, _) => { Output.CopyToClipboard(img); ToastWindow.Show("Copied to the clipboard", null, null, true, null, 2); Dismiss(); };
        Button Icon(string icon, string tip, Action a)
        {
            var b = new Button { Style = (Style)FindResource("IconButton"), Content = Ui.Icon(icon, 20), ToolTip = tip, Width = 38, Height = 38 };
            b.Click += (_, _) => a();
            return b;
        }
        var save = Icon("Save", "Save to the screenshots folder", () =>
        {
            var p = Output.SaveToFolder(img, out var err);
            ToastWindow.Show(p != null ? "Saved to " + new System.IO.DirectoryInfo(Settings.Current.Folder).Name : "Can't save the image", p != null ? System.IO.Path.GetFileName(p) : err, null, p != null);
            Dismiss();
        });
        var pin = Icon("Pin", "Pin to screen", () => { new PinWindow(img).Show(); Dismiss(); });
        var drag = Icon("Drag", "Drag the image into another app", () => { });
        Point? down = null;
        drag.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(drag);
        drag.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || down == null) return;
            if ((e.GetPosition(drag) - down.Value).Length < 6) return;
            down = null;
            Output.DragOut(drag, img);
        };
        var close = Icon("Close", "Dismiss", Dismiss);

        var size = new TextBlock { Text = $"{img.PixelWidth} × {img.PixelHeight}", Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(copy);
        var mid = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        mid.Children.Add(save); mid.Children.Add(pin); mid.Children.Add(drag);
        Grid.SetColumn(mid, 1);
        row.Children.Add(mid);
        var end = new StackPanel { Orientation = Orientation.Horizontal };
        end.Children.Add(size); end.Children.Add(close);
        Grid.SetColumn(end, 2);
        row.Children.Add(end);

        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(row);
        var bar = new Grid { Height = 2, Margin = new Thickness(0, 12, 0, 0) };
        bar.Children.Add(new Border { Background = Ui.B("Bg4Brush"), CornerRadius = new CornerRadius(1) });
        bar.Children.Add(progress);
        bar.SizeChanged += (_, e) => barWidth = e.NewSize.Width;
        stack.Children.Add(bar);

        Content = new Border
        {
            Background = Ui.B("MenuBrush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusLg"),
            Padding = new Thickness(14), Margin = new Thickness(18, 18, 18, 22), Child = stack, Effect = (System.Windows.Media.Effects.Effect)FindResource("PopoverShadow"),
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth; Top = wa.Bottom - ActualHeight;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            tick.Start();
        };
        tick.Tick += (_, _) =>
        {
            if (hovered) return;
            elapsed += tick.Interval.TotalMilliseconds;
            progress.Width = Math.Max(0, barWidth * (1 - elapsed / total));
            if (elapsed >= total) Dismiss();
        };
        MouseEnter += (_, _) => hovered = true;
        MouseLeave += (_, _) => hovered = false;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Dismiss(); };
    }

    void OpenEditor()
    {
        var ed = new EditorWindow(img);
        ed.Show();
        ed.Activate();
        Dismiss();
    }

    void Dismiss()
    {
        if (closing) return;
        closing = true;
        tick.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}

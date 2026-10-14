using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Shutter;

/// <summary>One row of the tray menu; a null entry is a separator.</summary>
public sealed record TrayEntry(string Icon, string Label, string Keys, Action Run);

/// <summary>The styled replacement for the standard tray context menu.</summary>
public sealed class TrayMenuWindow : Window
{
    static TrayMenuWindow? current;
    readonly System.Drawing.Point cursor;
    bool closing;

    public static void Open(IReadOnlyList<TrayEntry?> entries)
    {
        current?.CloseMenu();
        current = new TrayMenuWindow(entries);
        current.Show();
    }

    TrayMenuWindow(IReadOnlyList<TrayEntry?> entries)
    {
        cursor = Forms.Cursor.Position;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 13;
        Foreground = Ui.B("TextBrush");
        Title = "Shutter menu";

        var stack = new StackPanel { Width = 340 };

        // header
        var header = new Grid { Margin = new Thickness(10, 8, 10, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new ApertureLogo { Width = 36, Height = 36, WithTile = true });
        var ht = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ht.Children.Add(new TextBlock { Text = "Shutter", FontWeight = FontWeights.SemiBold, FontSize = 14 });
        ht.Children.Add(new TextBlock { Text = "Click the tray icon to capture an area", Foreground = Ui.B("Text3Brush"), FontSize = 12 });
        Grid.SetColumn(ht, 1);
        header.Children.Add(ht);
        stack.Children.Add(header);
        stack.Children.Add(Sep());

        foreach (var e in entries)
        {
            if (e == null) { stack.Children.Add(Sep()); continue; }
            stack.Children.Add(Row(e));
        }

        Content = new Border
        {
            Background = Ui.B("MenuBrush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusLg"),
            Padding = new Thickness(8), Margin = new Thickness(16), Child = stack, Effect = (System.Windows.Media.Effects.Effect)Application.Current.FindResource("PopoverShadow"),
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
        };
        ContentRendered += (_, _) => { Place(); Activate(); Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); };
        Deactivated += (_, _) => CloseMenu();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseMenu(); };
        Closed += (_, _) => { if (ReferenceEquals(current, this)) current = null; };
    }

    static FrameworkElement Sep() => new Border { Height = 1, Background = Ui.B("Line2Brush"), Margin = new Thickness(6, 6, 6, 6) };

    FrameworkElement Row(TrayEntry e)
    {
        var g = new Grid { Height = 38, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(e.Icon, 18, Ui.B("Text2Brush"));
        icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(icon);
        var label = new TextBlock { Text = e.Label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        g.Children.Add(label);
        if (!string.IsNullOrEmpty(e.Keys))
        {
            var k = new TextBlock { Text = e.Keys.Replace("+", " "), Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 10, 0) };
            Grid.SetColumn(k, 2);
            g.Children.Add(k);
        }
        var host = new Border { CornerRadius = Ui.Radius("RadiusMd"), Child = g, Background = Brushes.Transparent };
        host.MouseEnter += (_, _) => host.Background = Ui.B("Bg4Brush");
        host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
        host.MouseLeftButtonUp += (_, _) =>
        {
            CloseMenu();
            Application.Current.Dispatcher.BeginInvoke(new Action(e.Run), System.Windows.Threading.DispatcherPriority.Background);
        };
        return host;
    }

    void CloseMenu()
    {
        if (closing) return;
        closing = true;
        try { Close(); } catch { }
    }

    /// <summary>Open just above the tray icon (or below if the taskbar is on top), kept inside the work area.</summary>
    void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(ActualWidth * s), h = (int)Math.Ceiling(ActualHeight * s);
        var wa = Forms.Screen.FromPoint(cursor).WorkingArea;
        int x = Math.Clamp(cursor.X - w / 2, wa.Left, Math.Max(wa.Left, wa.Right - w));
        int y = cursor.Y - h;
        if (y < wa.Top) y = Math.Min(cursor.Y, wa.Bottom - h);
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_SHOWWINDOW);
    }
}

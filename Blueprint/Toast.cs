using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Blueprint;

/// <summary>Dark toast card in the bottom-right corner (replaces the tray balloon). Never takes focus.</summary>
public sealed class ToastWindow : Window
{
    static readonly List<ToastWindow> open = new();
    readonly DispatcherTimer timer;
    bool closing;

    /// <param name="keys">Optional shortcut shown as keycaps, e.g. "Ctrl+Shift+4".</param>
    public static void Show(string title, string? subtitle = null, BitmapSource? thumb = null, bool ok = false, string? keys = null, double seconds = 3.6)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            // keep at most 3 on screen
            // retire the oldest toasts (removed from the list immediately, so this loop always terminates)
            while (open.Count >= 3) { var oldest = open[0]; open.RemoveAt(0); oldest.Dismiss(); }
            new ToastWindow(title, subtitle, thumb, ok, keys, seconds).ShowInactive();
        }));
    }

    ToastWindow(string title, string? subtitle, BitmapSource? thumb, bool ok, string? keys, double seconds)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 13;
        Foreground = Ui.B("TextBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement lead;
        if (thumb != null)
        {
            lead = new Border
            {
                Width = 66, Height = 46, CornerRadius = new CornerRadius(6), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), ClipToBounds = true,
                Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill },
            };
        }
        else
        {
            lead = new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Background = Ui.B("Bg2Brush"),
                Child = new ApertureLogo { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
        }
        lead.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(lead);

        var text = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 200, MaxWidth = 300 };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        if (ok) { var tick = Ui.Icon("Check", 16, Ui.B("SuccessBrush")); tick.Margin = new Thickness(0, 0, 8, 0); tick.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(tick); }
        head.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(head);
        if (!string.IsNullOrEmpty(subtitle) || !string.IsNullOrEmpty(keys))
        {
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            if (!string.IsNullOrEmpty(subtitle)) sub.Children.Add(new TextBlock { Text = subtitle, Foreground = Ui.B("Text2Brush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 280, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(keys)) { var k = Ui.Keys(keys); k.Margin = new Thickness(6, 0, 0, 0); sub.Children.Add(k); }
            text.Children.Add(sub);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var close = new Button { Style = (Style)Application.Current.FindResource("IconButton"), Width = 26, Height = 26, Content = Ui.Icon("Close", 14), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Dismiss" };
        close.Click += (_, _) => Dismiss();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        Content = new Border
        {
            Background = Ui.B("MenuBrush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusLg"),
            Padding = new Thickness(14), Margin = new Thickness(16, 16, 16, 20), Child = grid, Effect = (System.Windows.Media.Effects.Effect)Application.Current.FindResource("PopoverShadow"),
        };

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => timer.Stop();
        MouseLeave += (_, _) => { if (!closing) { timer.Stop(); timer.Start(); } };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) =>
        {
            open.Add(this);
            Reposition();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            timer.Start();
        };
        Closed += (_, _) => { open.Remove(this); Reposition(); };
    }

    void ShowInactive() => Show();

    void Dismiss()
    {
        if (closing) return;
        closing = true;
        open.Remove(this);
        Reposition();
        timer.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    static void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        double bottom = wa.Bottom;
        for (int i = open.Count - 1; i >= 0; i--)
        {
            var w = open[i];
            w.Left = wa.Right - w.ActualWidth;
            w.Top = bottom - w.ActualHeight;
            bottom = w.Top + 8; // the card carries its own margin; tuck the next one just above
        }
    }
}

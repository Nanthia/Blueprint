using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Blueprint;

/// <summary>Preferences: sidebar navigation, setting rows with immediate apply, find-a-setting search, hotkey recorder.</summary>
public sealed class SettingsWindow : BlueprintWindow
{
    sealed record Row(string Page, string Section, string Icon, string Title, string? Subtitle, Func<FrameworkElement> Control, bool Tall = false);

    static readonly (string page, string icon)[] Pages =
    {
        ("General", "General"), ("Capture", "Camera"), ("Saving", "Save"), ("Editor", "Edit"), ("Text & search", "Ocr"), ("Hotkeys", "Keyboard"),
    };

    static readonly (string id, string label, string icon)[] HotkeyRows =
    {
        ("Area", "Area capture", "Area"), ("Scroll", "Scrolling capture", "Scrolling"), ("Fullscreen", "Fullscreen", "Fullscreen"),
        ("Delayed", "Delayed (3s)", "Timer"), ("Repeat", "Repeat last area", "Repeat"), ("Window", "Active window", "Window"),
        ("AnyWindow", "Any window", "Anywindow"), ("Clipboard", "Load from clipboard", "Clipboard"),
        ("Ocr", "Recognize text / QR", "Ocr"), ("Library", "Library", "Library"),
    };

    readonly Action onChanged;
    readonly List<Row> rows = new();
    readonly StackPanel content = new() { Margin = new Thickness(36, 28, 36, 28) };
    readonly TextBox search = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
    readonly Dictionary<string, RadioButton> nav = new();
    string current = "General";

    public SettingsWindow(Action onChanged)
    {
        this.onChanged = onChanged;
        Title = "Preferences";
        Width = 960; Height = 660; MinWidth = 780; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowMinimize = false;
        BuildRows();

        var sidebar = BuildSidebar();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content, Background = Ui.B("Bg1Brush") };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(sidebar);
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);
        Content = root;

        search.TextChanged += (_, _) => Render();
        Render();
    }

    // ---------- chrome ----------

    FrameworkElement BuildSidebar()
    {
        var panel = new DockPanel { Background = Ui.B("Bg2Brush") };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 18, 16) };
        footer.Children.Add(new ApertureLogo { Width = 22, Height = 22 });
        footer.Children.Add(new TextBlock { Text = "Blueprint " + AppVersion(), Foreground = Ui.B("Text3Brush"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);

        var searchBox = new Border
        {
            Background = Ui.B("Bg0Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusMd"),
            Margin = new Thickness(14, 14, 14, 10), Height = 38, Padding = new Thickness(12, 0, 10, 0),
        };
        var sg = new Grid();
        sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sg.ColumnDefinitions.Add(new ColumnDefinition());
        var si = Ui.Icon("Search", 16, Ui.B("Text3Brush"));
        si.VerticalAlignment = VerticalAlignment.Center;
        sg.Children.Add(si);
        search.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(search, 1);
        sg.Children.Add(search);
        var hint = new TextBlock { Text = "Find a setting", Foreground = Ui.B("Text3Brush"), IsHitTestVisible = false, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(hint, 1);
        sg.Children.Add(hint);
        search.TextChanged += (_, _) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchBox.Child = sg;
        DockPanel.SetDock(searchBox, Dock.Top);
        panel.Children.Add(searchBox);

        var list = new StackPanel { Margin = new Thickness(16, 4, 14, 0) };
        foreach (var (page, icon) in Pages)
        {
            var rb = new RadioButton { Style = (Style)FindResource("NavItem"), GroupName = "prefsnav", IsChecked = page == current };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Ui.Icon(icon, 18));
            sp.Children.Add(new TextBlock { Text = page, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            rb.Content = sp;
            var p = page;
            rb.Checked += (_, _) => { current = p; if (search.Text.Length > 0) search.Text = ""; else Render(); };
            nav[page] = rb;
            list.Children.Add(rb);
        }
        panel.Children.Add(list);
        return new Border { Child = panel, Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 0, 1, 0) };
    }

    // ---------- rows ----------

    static CheckBox Toggle(Func<bool> get, Action<bool> set, Action after)
    {
        var cb = new CheckBox { Style = (Style)Application.Current.FindResource("SwitchCheckBox"), IsChecked = get() };
        cb.Click += (_, _) => { set(cb.IsChecked == true); Settings.Current.Save(); after(); };
        return cb;
    }

    static FrameworkElement Segmented(string[] items, Func<string> get, Action<string> set, Action after)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        string group = "seg" + Guid.NewGuid();
        foreach (var it in items)
        {
            var rb = new RadioButton { Style = (Style)Application.Current.FindResource("SegmentItem"), Content = it, GroupName = group, IsChecked = it == get() };
            var v = it;
            rb.Checked += (_, _) => { set(v); Settings.Current.Save(); after(); };
            sp.Children.Add(rb);
        }
        return new Border { Style = (Style)Application.Current.FindResource("SegmentBox"), Child = sp };
    }

    static ComboBox Combo(IEnumerable<string> items, Func<string> get, Action<string> set, Action after, double width = 170)
    {
        var cb = new ComboBox { ItemsSource = items.ToList(), Width = width };
        cb.SelectedItem = get();
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is string s) { set(s); Settings.Current.Save(); after(); } };
        return cb;
    }

    void BuildRows()
    {
        var s = Settings.Current;
        Action nop = () => { };

        // General
        rows.Add(new Row("General", "Startup", "Power", "Launch at Windows startup", "Blueprint starts in the tray when you sign in",
            () => Toggle(() => s.StartWithWindows, v => s.StartWithWindows = v, nop)));

        // Capture
        rows.Add(new Row("Capture", "After a capture", "Copy", "Copy to the clipboard", "The image is ready to paste right away",
            () => Toggle(() => s.AfterGrabCopy, v => s.AfterGrabCopy = v, nop)));
        rows.Add(new Row("Capture", "After a capture", "Save", "Save to the screenshots folder", "Saved as PNG in the folder chosen under Saving",
            () => Toggle(() => s.AfterGrabSave, v => s.AfterGrabSave = v, nop)));
        rows.Add(new Row("Capture", "After a capture", "Image", "Then show", "What opens once the capture is done",
            () => Combo(new[] { "Editor", "Preview", "Nothing" }, () => s.ShowMode, v => s.ShowMode = v, nop)));
        rows.Add(new Row("Capture", "Scrolling capture", "Scrolling", "Maximum height", "Capture stops when the stitched image reaches this height",
            () =>
            {
                var tb = new TextBox { Width = 110, Text = s.ScrollMaxHeight.ToString(), HorizontalContentAlignment = HorizontalAlignment.Left };
                var unit = new TextBlock { Text = "px", Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };
                tb.LostFocus += (_, _) => { if (int.TryParse(tb.Text, out var n)) { s.ScrollMaxHeight = Math.Clamp(n, 1000, 60000); tb.Text = s.ScrollMaxHeight.ToString(); s.Save(); } else tb.Text = s.ScrollMaxHeight.ToString(); };
                var g = new Grid { Width = 110 }; g.Children.Add(tb); g.Children.Add(unit);
                return g;
            }));

        // Saving
        rows.Add(new Row("Saving", "Location", "Folder", "Screenshots folder", "Where saved screenshots go, and where the Library looks",
            () =>
            {
                var tb = new TextBox { Width = 300, Text = s.Folder, IsReadOnly = false };
                tb.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(tb.Text)) { s.Folder = tb.Text.Trim(); s.Save(); } };
                var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
                browse.Click += (_, _) =>
                {
                    using var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = s.Folder };
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { s.Folder = d.SelectedPath; tb.Text = d.SelectedPath; s.Save(); }
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(tb); sp.Children.Add(browse);
                return sp;
            }));
        rows.Add(new Row("Saving", "Format", "Image", "File format", "Auto uses PNG, and JPEG for very large images",
            () => Segmented(new[] { "Auto", "PNG" }, () => s.Format == "png" ? "PNG" : "Auto", v => s.Format = v == "PNG" ? "png" : "auto", nop)));

        // Editor
        rows.Add(new Row("Editor", "", "Copy", "Esc copies the image", "Pressing Esc copies the result before the editor closes",
            () => Toggle(() => s.EscCopy, v => s.EscCopy = v, nop)));
        rows.Add(new Row("Editor", "", "Save", "Esc also saves", "Saves a PNG to your screenshots folder",
            () => Toggle(() => s.EscSave, v => s.EscSave = v, nop)));
        rows.Add(new Row("Editor", "", "Pin", "Keep the editor on top", null,
            () => Toggle(() => s.AlwaysOnTop, v => s.AlwaysOnTop = v, nop)));
        rows.Add(new Row("Editor", "", "Picker", "Color picker copies as", null,
            () => Segmented(new[] { "HEX", "RGB", "HSL" }, () => s.ColorFormat.ToUpperInvariant(), v => s.ColorFormat = v.ToLowerInvariant(), nop)));

        // Text & search
        rows.Add(new Row("Text & search", "Library", "Search", "Index text in saved screenshots", "Lets the Library search for words inside your images (stays on this PC)",
            () => Toggle(() => s.IndexText, v => s.IndexText = v, nop)));
        rows.Add(new Row("Text & search", "Recognition", "Ocr", "Remove line breaks", "Join recognized lines into one paragraph",
            () => Toggle(() => s.RemoveBreaks, v => s.RemoveBreaks = v, nop)));
        rows.Add(new Row("Text & search", "Recognition", "Globe", "Recognition language", "Auto uses the languages installed in Windows",
            () =>
            {
                var langs = new List<string> { "auto" };
                langs.AddRange(Ocr.AvailableLanguages());
                return Combo(langs, () => langs.Contains(s.OcrLanguage) ? s.OcrLanguage : "auto", v => s.OcrLanguage = v, nop, 150);
            }));

        // Hotkeys
        foreach (var (id, label, icon) in HotkeyRows)
        {
            var hid = id;
            rows.Add(new Row("Hotkeys", "", icon, label, null, () => new HotkeyBox(
                () => s.Hotkeys.GetValueOrDefault(hid, ""),
                v =>
                {
                    // a combination can only belong to one action: take it from whichever action had it
                    bool stolen = false;
                    if (v.Length > 0)
                        foreach (var k in s.Hotkeys.Keys.ToList())
                            if (k != hid && string.Equals(s.Hotkeys[k], v, StringComparison.OrdinalIgnoreCase)) { s.Hotkeys[k] = ""; stolen = true; }
                    s.Hotkeys[hid] = v; s.Save(); onChanged();
                    if (stolen) Dispatcher.BeginInvoke(new Action(Render));
                })));
        }
    }

    // ---------- rendering ----------

    void Render()
    {
        content.Children.Clear();
        var query = search.Text.Trim();
        if (query.Length > 0)
        {
            content.Children.Add(Header("Search results"));
            var hits = rows.Where(r => (r.Title + " " + r.Subtitle + " " + r.Page + " " + r.Section).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0)
                content.Children.Add(new TextBlock { Text = "No settings match “" + query + "”", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 8, 0, 0) });
            else content.Children.Add(Card(hits, showPage: true));
            return;
        }

        var pageRows = rows.Where(r => r.Page == current).ToList();
        var header = Header(current);
        if (current == "Hotkeys")
        {
            var g = new Grid();
            g.Children.Add(header);
            var restore = new Button { Content = Ui.IconLabel("Reset", "Restore defaults"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            restore.Click += (_, _) => { Settings.Current.Hotkeys = Settings.DefaultHotkeys(); Settings.Current.Save(); onChanged(); Render(); };
            g.Children.Add(restore);
            content.Children.Add(g);
        }
        else content.Children.Add(header);

        foreach (var grp in pageRows.GroupBy(r => r.Section))
        {
            if (!string.IsNullOrEmpty(grp.Key))
                content.Children.Add(new TextBlock { Text = grp.Key, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) });
            else content.Children.Add(new Border { Height = 14 });
            content.Children.Add(Card(grp.ToList(), showPage: false));
        }

        if (current == "Hotkeys")
        {
            var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 14, 0, 0) };
            note.Children.Add(Ui.Icon("Info", 15, Ui.B("Text3Brush")));
            note.Children.Add(new TextBlock { Text = "Click a box and press the keys.", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            note.Children.Add(Ui.Keycap("Backspace"));
            note.Children.Add(new TextBlock { Text = "clears a shortcut.", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(note);
        }

        if (current == "General") AboutCard();
    }

    static string AppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    static TextBlock Header(string text) => new() { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };

    FrameworkElement Card(List<Row> list, bool showPage)
    {
        var stack = new StackPanel();
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var grid = new Grid { Margin = new Thickness(16, 0, 16, 0), MinHeight = 56 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = Ui.Icon(r.Icon, 18, Ui.B("Text2Brush"));
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 16, 10) };
            text.Children.Add(new TextBlock { Text = r.Title, FontSize = 13.5 });
            var sub = r.Subtitle ?? (showPage ? r.Page : null);
            if (sub != null) text.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var ctl = r.Control();
            ctl.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ctl, 2);
            grid.Children.Add(ctl);

            stack.Children.Add(grid);
            if (i < list.Count - 1) stack.Children.Add(new Border { Height = 1, Background = Ui.B("LineBrush"), Margin = new Thickness(16, 0, 0, 0) });
        }
        return new Border { Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = stack };
    }

    FrameworkElement AboutCard()
    {
        content.Children.Add(new TextBlock { Text = "About", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) });
        var g = new Grid { Margin = new Thickness(16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new ApertureLogo { Width = 44, Height = 44, WithTile = true });
        var t = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = "Blueprint", FontSize = 14, FontWeight = FontWeights.SemiBold });
        t.Children.Add(new TextBlock { Text = "Settings and logs are stored in " + Settings.Dir, FontSize = 12, Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var open = new Button { Content = Ui.IconLabel("Folder", "Open folder") };
        open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Settings.Dir + "\"") { UseShellExecute = true }); } catch { } };
        Grid.SetColumn(open, 2);
        g.Children.Add(open);
        var card = new Border { Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = g };
        content.Children.Add(card);
        return card;
    }
}

/// <summary>Click, then press a combination. Shows keycaps; Backspace or × clears; Esc cancels.</summary>
public sealed class HotkeyBox : Border
{
    readonly Func<string> get;
    readonly Action<string> set;
    bool recording;

    public HotkeyBox(Func<string> get, Action<string> set)
    {
        this.get = get; this.set = set;
        Background = Ui.B("Bg0Brush");
        BorderBrush = Ui.B("Line2Brush");
        BorderThickness = new Thickness(1);
        CornerRadius = Ui.Radius("RadiusMd");
        Padding = new Thickness(8, 4, 8, 4);
        MinWidth = 200; MinHeight = 36;
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) != null) return; // the clear (×) button
            Keyboard.Focus(this);
            if (!recording) { recording = true; HotkeyService.Pause(true); Refresh(); }
            e.Handled = true;
        };
        GotKeyboardFocus += (_, _) => { if (!recording) { recording = true; HotkeyService.Pause(true); Refresh(); } };
        LostKeyboardFocus += (_, _) => { if (recording) { recording = false; HotkeyService.Pause(false); Refresh(); } };
        PreviewKeyDown += OnKey;
        Unloaded += (_, _) => { if (recording) { recording = false; HotkeyService.Pause(false); } };
        Refresh();
    }

    static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null) { if (d is T t) return t; d = System.Windows.Media.VisualTreeHelper.GetParent(d); }
        return null;
    }

    void Refresh()
    {
        BorderBrush = recording ? Ui.B("AccentBrush") : Ui.B("Line2Brush");
        BorderThickness = new Thickness(recording ? 1.5 : 1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cur = get();
        if (recording)
        {
            row.Children.Add(new TextBlock { Text = "Press a shortcut…", Foreground = Ui.B("AccentTextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) });
            row.Children.Add(Ui.Keycap("Esc"));
        }
        else if (string.IsNullOrEmpty(cur))
            row.Children.Add(new TextBlock { Text = "Not set", Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        else
        {
            row.Children.Add(Ui.Keys(cur));
            var x = new Button { Style = (Style)FindResource("IconButton"), Width = 24, Height = 24, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Clear" };
            x.Content = Ui.Icon("Close", 14);
            x.Click += (_, _) => { set(""); Refresh(); };
            row.Children.Add(x);
        }
        Child = row;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (!recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None) return;
        if (key == Key.Escape) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null); return; }
        if (key is Key.Back or Key.Delete) { set(""); Keyboard.ClearFocus(); recording = false; Refresh(); return; }
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && key != Key.PrintScreen && !(key >= Key.F1 && key <= Key.F24)) return; // avoid bare letters
        set(HotkeyService.Format(mods, key));
        recording = false;
        Refresh();
        Keyboard.ClearFocus();
    }
}

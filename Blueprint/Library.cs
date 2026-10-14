using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Blueprint;

/// <summary>Cached OCR text per file (path + modified ticks), so search can look inside screenshots.</summary>
static class LibraryIndex
{
    sealed class Entry { public long Ticks { get; set; } public string Text { get; set; } = ""; }

    static readonly object gate = new();
    static readonly string file = System.IO.Path.Combine(Settings.Dir, "library-index.json");
    static Dictionary<string, Entry> map = Load();

    static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(file)) ?? new();
        }
        catch { }
        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    public static string? Get(string path, long ticks)
    {
        lock (gate) return map.TryGetValue(path, out var e) && e.Ticks == ticks ? e.Text : null;
    }

    public static void Set(string path, long ticks, string text)
    {
        lock (gate) map[path] = new Entry { Ticks = ticks, Text = text };
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            string json;
            lock (gate) json = JsonSerializer.Serialize(map);
            File.WriteAllText(file, json);
        }
        catch { }
    }
}

public sealed class LibItem : INotifyPropertyChanged
{
    public required string Path { get; init; }
    public required DateTime Date { get; init; }
    public required long Ticks { get; init; }
    public long Bytes { get; init; }
    public string? Text { get; set; }   // null = not indexed yet
    public bool Loading { get; set; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>"Today, 09:41", "Yesterday, 17:22", "Mon, 14:08", "9 Oct, 10:12".</summary>
    public string Friendly
    {
        get
        {
            var now = DateTime.Now;
            if (Date.Date == now.Date) return "Today, " + Date.ToString("HH:mm");
            if (Date.Date == now.Date.AddDays(-1)) return "Yesterday, " + Date.ToString("HH:mm");
            if ((now.Date - Date.Date).TotalDays < 7) return Date.ToString("ddd, HH:mm");
            return Date.ToString("d MMM, HH:mm");
        }
    }

    BitmapSource? thumb;
    string dims = "";
    public BitmapSource? Thumb { get => thumb; set { thumb = value; Raise(nameof(Thumb)); } }
    public string Dims { get => dims; set { dims = value; Raise(nameof(Tip)); } }
    public string Tip => $"{System.IO.Path.GetFileName(Path)}\n{Dims}   {Bytes / 1024.0:0} KB\n{Date:F}";

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Gallery of the screenshots in the default folder, with filename + in-image text search.</summary>
public sealed class LibraryWindow : BlueprintWindow
{
    static LibraryWindow? instance;

    public static void ShowOrActivate()
    {
        if (instance == null)
        {
            instance = new LibraryWindow();
            instance.Closed += (_, _) => instance = null;
            instance.Show();
        }
        else
        {
            if (instance.WindowState == WindowState.Minimized) instance.WindowState = WindowState.Normal;
            instance.Activate();
            instance.Refresh();
        }
    }

    readonly ObservableCollection<LibItem> shown = new();
    List<LibItem> all = new();
    readonly TextBox search = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock count = new() { Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    readonly ListBox list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended };
    readonly Grid emptyHost = new() { Visibility = Visibility.Collapsed };
    readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly SemaphoreSlim thumbGate = new(3);
    FileSystemWatcher? watcher;
    string? watched;
    CancellationTokenSource? indexCts;
    int indexTotal, indexDone;
    string range = "All";
    string sort = "Newest first";

    LibraryWindow()
    {
        Title = "Library";
        Width = 1100; Height = 740; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // title bar: search in the middle, count + Open folder + more on the right
        var box = new Border
        {
            Background = Ui.B("Bg0Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusMd"),
            Width = 440, Height = 32, Padding = new Thickness(10, 0, 8, 0), Margin = new Thickness(18, 0, 0, 0),
        };
        var sg = new Grid();
        sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sg.ColumnDefinitions.Add(new ColumnDefinition());
        sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var si = Ui.Icon("Search", 16, Ui.B("Text3Brush")); si.VerticalAlignment = VerticalAlignment.Center;
        sg.Children.Add(si);
        search.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(search, 1);
        sg.Children.Add(search);
        var hint = new TextBlock { Text = "Search file names and text inside screenshots", Foreground = Ui.B("Text3Brush"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(hint, 1);
        sg.Children.Add(hint);
        var kc = Ui.Keys("Ctrl+F"); Grid.SetColumn(kc, 2);
        sg.Children.Add(kc);
        search.TextChanged += (_, _) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; searchTimer.Stop(); searchTimer.Start(); };
        box.Child = sg;
        TitleLeft = box;

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(count);
        var openFolder = new Button { Content = Ui.IconLabel("Folder", "Open folder", 16, 8), Height = 32, ToolTip = "Open the screenshots folder" };
        openFolder.Click += (_, _) => OpenFolder();
        right.Children.Add(openFolder);
        var more = new Button { Style = (Style)FindResource("IconButton"), Content = Ui.Icon("More", 18), Margin = new Thickness(6, 0, 0, 0), ToolTip = "More" };
        more.Click += (_, _) =>
        {
            var m = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            var ch = new MenuItem { Header = "Change folder…", Icon = Ui.Icon("Folder", 16, Ui.B("Text2Brush")) }; ch.Click += (_, _) => ChangeFolder();
            m.Items.Add(ch);
            m.IsOpen = true;
        };
        right.Children.Add(more);
        TitleRight = right;

        // filter bar
        var filterBar = new Grid { Margin = new Thickness(24, 16, 24, 6) };
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filterBar.ColumnDefinitions.Add(new ColumnDefinition());
        filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var t in new[] { "All", "Today", "This week", "This month" })
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegmentItem"), GroupName = "librange", Content = t, IsChecked = t == range, Padding = new Thickness(14, 6, 14, 6) };
            var tt = t;
            rb.Checked += (_, _) => { range = tt; ApplyFilter(); };
            tabs.Children.Add(rb);
        }
        filterBar.Children.Add(new Border { Style = (Style)FindResource("SegmentBox"), Child = tabs });
        var sortRow = new StackPanel { Orientation = Orientation.Horizontal };
        sortRow.Children.Add(new TextBlock { Text = "Sort", Foreground = Ui.B("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        var sortBox = new ComboBox { ItemsSource = new[] { "Newest first", "Oldest first", "Name", "Size" }, SelectedItem = sort, MinWidth = 150 };
        sortBox.SelectionChanged += (_, _) => { if (sortBox.SelectedItem is string s) { sort = s; ApplyFilter(); } };
        sortRow.Children.Add(sortBox);
        Grid.SetColumn(sortRow, 2);
        filterBar.Children.Add(sortRow);

        // grid
        list.ItemsSource = shown;
        list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapPanel/></ItemsPanelTemplate>");
        list.ItemTemplate = (DataTemplate)XamlReader.Parse(ItemTemplateXaml);
        list.ItemContainerStyle = (Style)XamlReader.Parse(ItemStyleXaml);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.Margin = new Thickness(18, 4, 18, 12);
        list.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnCardButton));

        var body = new Grid();
        body.Children.Add(list);
        body.Children.Add(emptyHost);

        var root = new DockPanel();
        DockPanel.SetDock(filterBar, Dock.Top);
        root.Children.Add(filterBar);
        root.Children.Add(body);
        Content = root;

        list.MouseDoubleClick += (_, e) => { if (FindItem(e.OriginalSource as DependencyObject) != null) OpenSelected(); };
        list.PreviewKeyDown += OnKey;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { search.Focus(); e.Handled = true; } };
        list.ContextMenu = BuildMenu();
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); ApplyFilter(); };
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); Refresh(); };
        Activated += (_, _) => { if (watched != Settings.Current.Folder) Refresh(); };
        Closed += (_, _) => { indexCts?.Cancel(); watcher?.Dispose(); LibraryIndex.Save(); };
        Loaded += (_, _) => { Refresh(); };
    }

    const string NS = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:Blueprint;assembly=Blueprint'";

    static readonly string ItemTemplateXaml = $@"
<DataTemplate {NS}>
  <Border Width='210' Margin='4' Padding='8' Background='Transparent' ToolTip='{{Binding Tip}}'>
    <StackPanel>
      <Grid Height='134'>
        <Border Background='#111113' CornerRadius='8' BorderBrush='#12FFFFFF' BorderThickness='1' ClipToBounds='True'>
          <Image Source='{{Binding Thumb}}' Stretch='Uniform' RenderOptions.BitmapScalingMode='HighQuality' Margin='6'/>
        </Border>
        <StackPanel x:Name='actions' Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Top' Margin='6' Visibility='Collapsed'>
          <Button Tag='copy' Style='{{DynamicResource ThumbAction}}' ToolTip='Copy image'><local:IconPresenter Icon='Copy' Size='16'/></Button>
          <Button Tag='pin' Style='{{DynamicResource ThumbAction}}' ToolTip='Pin to screen' Margin='4,0,0,0'><local:IconPresenter Icon='Pin' Size='16'/></Button>
          <Button Tag='more' Style='{{DynamicResource ThumbAction}}' ToolTip='More' Margin='4,0,0,0'><local:IconPresenter Icon='More' Size='16'/></Button>
        </StackPanel>
      </Grid>
      <TextBlock Text='{{Binding Name}}' Foreground='#EDEDF0' FontSize='13' TextTrimming='CharacterEllipsis' Margin='2,9,2,0'/>
      <TextBlock Text='{{Binding Friendly}}' Foreground='#74747E' FontSize='12' Margin='2,2,2,0'/>
    </StackPanel>
  </Border>
  <DataTemplate.Triggers>
    <DataTrigger Binding='{{Binding RelativeSource={{RelativeSource AncestorType=ListBoxItem}}, Path=IsMouseOver}}' Value='True'>
      <Setter TargetName='actions' Property='Visibility' Value='Visible'/>
    </DataTrigger>
  </DataTemplate.Triggers>
</DataTemplate>";

    static readonly string ItemStyleXaml = $@"
<Style {NS} TargetType='ListBoxItem'>
  <Setter Property='FocusVisualStyle' Value='{{x:Null}}'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <Border x:Name='b' Background='Transparent' CornerRadius='12' BorderThickness='2' BorderBrush='Transparent'>
          <ContentPresenter/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#0DFFFFFF'/></Trigger>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='b' Property='BorderBrush' Value='#0A84FF'/>
            <Setter TargetName='b' Property='Background' Value='#290A84FF'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    ContextMenu BuildMenu()
    {
        var m = new ContextMenu();
        void Add(string header, string icon, string gesture, Action a, bool danger = false)
        {
            var i = new MenuItem { Header = header, InputGestureText = gesture, Icon = Ui.Icon(icon, 16, Ui.B(danger ? "DangerBrush" : "Text2Brush")) };
            if (danger) i.Style = (Style)FindResource("DangerMenuItem");
            i.Click += (_, _) => a();
            m.Items.Add(i);
        }
        Add("Open in editor", "Edit", "Enter", OpenSelected);
        Add("Copy image", "Copy", "Ctrl C", CopySelected);
        Add("Pin to screen", "Pin", "", PinSelected);
        Add("Show in folder", "Folder", "", () => { if (Selected() is { } s) Process.Start("explorer.exe", $"/select,\"{s.Path}\""); });
        m.Items.Add(new Separator());
        Add("Move to Recycle Bin", "Trash", "Del", DeleteSelected, danger: true);
        return m;
    }

    void OnCardButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button b || b.Tag is not string tag || b.DataContext is not LibItem item) return;
        e.Handled = true;
        list.SelectedItem = item;
        switch (tag)
        {
            case "copy": CopySelected(); break;
            case "pin": PinSelected(); break;
            case "more":
                var cm = list.ContextMenu!;
                cm.PlacementTarget = b; cm.Placement = PlacementMode.Bottom; cm.IsOpen = true;
                break;
        }
    }

    LibItem? Selected() => list.SelectedItem as LibItem;
    static ListBoxItem? FindItem(DependencyObject? d)
    {
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return d as ListBoxItem;
    }

    // ---------- data ----------

    void Refresh()
    {
        var folder = Settings.Current.Folder;
        SetupWatcher(folder);
        if (!Directory.Exists(folder))
        {
            all = new List<LibItem>();
            ApplyFilter();
            return;
        }
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        List<FileInfo> files;
        try { files = new DirectoryInfo(folder).EnumerateFiles().Where(f => exts.Contains(f.Extension)).OrderByDescending(f => f.LastWriteTime).ToList(); }
        catch { return; }

        var existing = all.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        var next = new List<LibItem>(files.Count);
        foreach (var f in files)
        {
            long ticks = f.LastWriteTimeUtc.Ticks;
            if (existing.TryGetValue(f.FullName, out var it) && it.Ticks == ticks) { next.Add(it); continue; }
            next.Add(new LibItem { Path = f.FullName, Date = f.LastWriteTime, Ticks = ticks, Bytes = f.Length, Text = LibraryIndex.Get(f.FullName, ticks) });
        }
        all = next;
        ApplyFilter();
        foreach (var it in all.Where(i => i.Thumb == null && !i.Loading).ToList()) _ = LoadThumbAsync(it, 0);
        StartIndexing();
    }

    void SetupWatcher(string folder)
    {
        if (watched == folder && watcher != null) return;
        watcher?.Dispose();
        watcher = null;
        watched = folder;
        if (!Directory.Exists(folder)) return;
        try
        {
            watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
            FileSystemEventHandler h = (_, _) => Dispatcher.BeginInvoke(new Action(() => { refreshTimer.Stop(); refreshTimer.Start(); }));
            watcher.Created += h; watcher.Deleted += h; watcher.Changed += h;
            watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(new Action(() => { refreshTimer.Stop(); refreshTimer.Start(); }));
        }
        catch { }
    }

    bool InRange(LibItem it)
    {
        var now = DateTime.Now;
        return range switch
        {
            "Today" => it.Date.Date == now.Date,
            "This week" => (now.Date - it.Date.Date).TotalDays < 7,
            "This month" => it.Date.Year == now.Year && it.Date.Month == now.Month,
            _ => true,
        };
    }

    void ApplyFilter()
    {
        var terms = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<LibItem> q = all.Where(InRange);
        if (terms.Length > 0)
            q = q.Where(it => { var hay = it.Name + " " + (it.Text ?? ""); return terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase)); });
        q = sort switch
        {
            "Oldest first" => q.OrderBy(i => i.Date),
            "Name" => q.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "Size" => q.OrderByDescending(i => i.Bytes),
            _ => q.OrderByDescending(i => i.Date),
        };
        var selected = list.SelectedItems.Cast<LibItem>().ToList();
        shown.Clear();
        foreach (var it in q) shown.Add(it);
        foreach (var s in selected.Where(shown.Contains)) list.SelectedItems.Add(s);
        UpdateStatus();
    }

    void UpdateStatus()
    {
        bool filtering = search.Text.Trim().Length > 0 || range != "All";
        string s = filtering ? $"{shown.Count} of {all.Count}" : $"{all.Count} screenshot{(all.Count == 1 ? "" : "s")}";
        if (indexTotal > 0 && indexDone < indexTotal) s += $"  ·  indexing text {indexDone}/{indexTotal}";
        count.Text = s;
        BuildEmpty();
    }

    void BuildEmpty()
    {
        emptyHost.Children.Clear();
        bool noFiles = all.Count == 0;
        bool noMatch = !noFiles && shown.Count == 0;
        emptyHost.Visibility = noFiles || noMatch ? Visibility.Visible : Visibility.Collapsed;
        if (!noFiles && !noMatch) return;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
        var tile = new Border
        {
            Width = 96, Height = 96, CornerRadius = new CornerRadius(26), Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.9,
            Child = new ApertureLogo { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.35 },
        };
        stack.Children.Add(tile);

        if (noFiles)
        {
            stack.Children.Add(new TextBlock { Text = "Screenshots you save will appear here", FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 12) });
            var watching = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            watching.Children.Add(new TextBlock { Text = "Blueprint is watching", Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            watching.Children.Add(new Border
            {
                Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusSm"), Padding = new Thickness(10, 4, 10, 4),
                Child = new TextBlock { Text = Settings.Current.Folder, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5 },
            });
            stack.Children.Add(watching);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
            var cap = new Button { Style = (Style)FindResource("PrimaryButton"), Height = 42, Padding = new Thickness(18, 0, 14, 0) };
            var capRow = new StackPanel { Orientation = Orientation.Horizontal };
            capRow.Children.Add(Ui.Icon("Area", 18));
            capRow.Children.Add(new TextBlock { Text = "Capture an area", Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
            var hk = Settings.Current.Hotkeys.GetValueOrDefault("Area", "");
            if (hk.Length > 0) capRow.Children.Add(new TextBlock { Text = hk.Replace("+", " "), Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
            cap.Content = capRow;
            cap.Click += (_, _) =>
            {
                Hide();
                void Back() { RegionSelector.Ended -= Back; Show(); }
                RegionSelector.Ended += Back;
                Dispatcher.BeginInvoke(new Action(() => RegionSelector.Start((rect, img) => { var ed = new EditorWindow(img); ed.Show(); })), DispatcherPriority.Background);
            };
            buttons.Children.Add(cap);
            var change = new Button { Content = "Change folder…", Height = 42, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(10, 0, 0, 0), FontSize = 14 };
            change.Click += (_, _) => ChangeFolder();
            buttons.Children.Add(change);
            stack.Children.Add(buttons);
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = "No matches", FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Try a different word, or widen the date range.", Foreground = Ui.B("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Center });
        }
        emptyHost.Children.Add(stack);
    }

    async Task LoadThumbAsync(LibItem it, int attempt)
    {
        it.Loading = true;
        await thumbGate.WaitAsync();
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    var dec = BitmapDecoder.Create(new Uri(it.Path), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    int pw = dec.Frames[0].PixelWidth, ph = dec.Frames[0].PixelHeight;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(it.Path);
                    bi.DecodePixelWidth = Math.Min(420, pw);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bi.EndInit();
                    bi.Freeze();
                    return ((BitmapSource?)bi, $"{pw} × {ph} px");
                }
                catch { return ((BitmapSource?)null, ""); }
            });
            if (result.Item1 == null && attempt < 2)
            {
                // the file may still be being written
                await Task.Delay(700);
                thumbGate.Release();
                try { await LoadThumbAsync(it, attempt + 1); } finally { await thumbGate.WaitAsync(); }
                return;
            }
            it.Thumb = result.Item1;
            it.Dims = result.Item2;
        }
        finally { thumbGate.Release(); it.Loading = false; }
    }

    // ---------- OCR text index ----------

    async void StartIndexing()
    {
        indexCts?.Cancel();
        var cts = indexCts = new CancellationTokenSource();
        if (!Settings.Current.IndexText) { indexTotal = indexDone = 0; UpdateStatus(); return; }
        var todo = all.Where(i => i.Text == null).ToList();
        indexTotal = todo.Count; indexDone = 0;
        UpdateStatus();
        if (todo.Count == 0) return;

        await Task.Run(async () =>
        {
            foreach (var it in todo)
            {
                if (cts.IsCancellationRequested) return;
                string text = await IndexOne(it.Path);
                LibraryIndex.Set(it.Path, it.Ticks, text);
                await Dispatcher.InvokeAsync(() =>
                {
                    it.Text = text;
                    indexDone++;
                    UpdateStatus();
                    if (search.Text.Length > 0 && indexDone % 5 == 0) ApplyFilter();
                });
            }
            LibraryIndex.Save();
        });
        if (!cts.IsCancellationRequested && search.Text.Length > 0) ApplyFilter();
    }

    static async Task<string> IndexOne(string path)
    {
        try
        {
            var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 25_000_000) return "";
            BitmapSource src = frame;
            if (frame.PixelWidth > 2600)
            {
                double k = 2600.0 / frame.PixelWidth;
                src = new TransformedBitmap(frame, new ScaleTransform(k, k));
            }
            src.Freeze();
            return await Ocr.TextOnlyAsync(src);
        }
        catch { return ""; }
    }

    // ---------- actions ----------

    static BitmapSource? LoadFull(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    void OpenSelected()
    {
        if (Selected() is not { } s) return;
        var img = LoadFull(s.Path);
        if (img == null) { ToastWindow.Show("Failed to open image file"); return; }
        var ed = new EditorWindow(img, s.Name);
        ed.Show();
        ed.Activate();
    }

    void CopySelected()
    {
        if (Selected() is { } s && LoadFull(s.Path) is { } img)
            ToastWindow.Show(Output.CopyToClipboard(img) ? "Copied to the clipboard" : "Can't copy image", s.Name, null, true);
    }

    void PinSelected()
    {
        if (Selected() is { } s && LoadFull(s.Path) is { } img) new PinWindow(img).Show();
    }

    void DeleteSelected()
    {
        var items = list.SelectedItems.Cast<LibItem>().ToList();
        if (items.Count == 0) return;
        var msg = items.Count == 1 ? $"Move \"{items[0].Name}\" to the Recycle Bin?" : $"Move {items.Count} screenshots to the Recycle Bin?";
        if (MessageBox.Show(this, msg, "Delete screenshot", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var it in items)
        {
            try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); }
            catch (Exception ex) { ToastWindow.Show("Can't delete", ex.Message); }
        }
        Refresh();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CopySelected(); e.Handled = true; }
    }

    void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.Current.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.Current.Folder + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    void ChangeFolder()
    {
        using var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = Settings.Current.Folder, Description = "Screenshot folder (also used for saving)" };
        if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Settings.Current.Folder = d.SelectedPath;
        Settings.Current.Save();
        all = new List<LibItem>();
        Refresh();
    }
}

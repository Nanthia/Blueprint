using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Shutter;

public partial class App : Application
{
    Mutex? mutex;
    Forms.NotifyIcon? tray;
    HotkeyService? hotkeys;
    Drawing.Rectangle? lastArea;
    SettingsWindow? settingsWin;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // developer helper: write the app .ico and exit
        var mi = Array.IndexOf(e.Args, "--make-icon");
        if (mi >= 0 && mi + 1 < e.Args.Length) { AppIcons.WriteIco(e.Args[mi + 1]); Shutdown(); return; }

        mutex = new Mutex(true, "Shutter.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) =>
        {
            try { File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now}: {ex.Exception}\n\n"); } catch { }
            Notify("Something went wrong: " + ex.Exception.Message);
            ex.Handled = true;
        };

        BuildTray();
        RegionSelector.Ended += () => SetCapturing(false);
        hotkeys = new HotkeyService(RunAction);
        RegisterHotkeys();
        Log("Running in the tray.");
        ToastWindow.Show("Shutter is running in the tray", "Capture an area with", keys: Settings.Current.Hotkeys.GetValueOrDefault("Area", ""));
        if (Array.Exists(e.Args, a => a == "--settings")) ShowSettings();
        if (Array.Exists(e.Args, a => a == "--library")) LibraryWindow.ShowOrActivate();
        if (Array.Exists(e.Args, a => a == "--menu")) OpenTrayMenu();
        if (Array.Exists(e.Args, a => a == "--toast")) ToastWindow.Show("Copied to the clipboard", "1200 × 700 · saved to Screenshots", ok: true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hotkeys?.Dispose();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        mutex?.Dispose();
        base.OnExit(e);
    }

    void RegisterHotkeys()
    {
        var failed = hotkeys?.Register(Settings.Current.Hotkeys);
        if (failed is { Count: > 0 })
            Notify("Couldn't register: " + string.Join(", ", failed) + " (already used by another app?)");
    }

    // ---------- tray ----------

    void BuildTray()
    {
        tray = new Forms.NotifyIcon { Icon = AppIcons.Tray(false), Text = "Shutter", Visible = true };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) RunAction("Area");
            else if (e.Button == Forms.MouseButtons.Right) Dispatcher.BeginInvoke(new Action(OpenTrayMenu));
        };
    }

    void SetCapturing(bool on)
    {
        if (tray == null) return;
        var old = tray.Icon;
        tray.Icon = AppIcons.Tray(on);
        try { old?.Dispose(); } catch { }
    }

    string Hk(string id) => Settings.Current.Hotkeys.GetValueOrDefault(id, "");

    void OpenTrayMenu()
    {
        TrayEntry E(string icon, string label, string id, Action a) => new(icon, label, id.Length > 0 ? Hk(id) : "", a);
        TrayMenuWindow.Open(new TrayEntry?[]
        {
            E("Area", "Area capture", "Area", () => RunAction("Area")),
            E("Scrolling", "Scrolling capture", "Scroll", () => RunAction("Scroll")),
            E("Fullscreen", "Fullscreen", "Fullscreen", () => RunAction("Fullscreen")),
            E("Timer", "Delayed screenshot (3s)", "Delayed", () => RunAction("Delayed")),
            E("Repeat", "Repeat area capture", "Repeat", () => RunAction("Repeat")),
            null,
            E("Window", "Active window", "Window", () => RunAction("Window")),
            E("Anywindow", "Any window…", "AnyWindow", () => RunAction("AnyWindow")),
            null,
            E("Ocr", "Recognize text / QR", "Ocr", () => RunAction("Ocr")),
            E("Clipboard", "Load from clipboard", "Clipboard", () => RunAction("Clipboard")),
            null,
            E("Library", "Library…", "Library", () => RunAction("Library")),
            E("Folder", "Open screenshots folder", "", OpenFolder),
            E("Settings", "Preferences…", "", ShowSettings),
            null,
            E("Power", "Quit Shutter", "", Shutdown),
        });
    }

    static void Log(string msg)
    {
        try { Directory.CreateDirectory(Settings.Dir); File.AppendAllText(Path.Combine(Settings.Dir, "app.log"), $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
    }

    /// <summary>Plain one-line toast (errors, hints). Also written to the log.</summary>
    void Notify(string msg)
    {
        Log(msg);
        ToastWindow.Show(msg);
    }

    void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Settings.Current.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.Current.Folder + "\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Notify("Folder doesn't exist: " + ex.Message); }
    }

    void ShowSettings()
    {
        if (settingsWin != null) { settingsWin.Activate(); return; }
        settingsWin = new SettingsWindow(RegisterHotkeys);
        settingsWin.Closed += (_, _) => settingsWin = null;
        settingsWin.Show();
        settingsWin.Activate();
    }

    // ---------- actions ----------

    void RunAction(string name)
    {
        Log("action " + name);
        switch (name)
        {
            case "Area":
                SetCapturing(true);
                RegionSelector.Start((rect, img) => { lastArea = rect; AfterCapture(img); });
                break;
            case "Fullscreen":
                AfterCapture(ScreenCapture.GrabCursorScreen());
                break;
            case "Delayed":
                ToastWindow.Show("Capturing in 3 seconds…", seconds: 2.4);
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (_, _) => { t.Stop(); AfterCapture(ScreenCapture.GrabCursorScreen()); };
                t.Start();
                break;
            case "Repeat":
                if (lastArea is not { } r) { Notify("No area to repeat"); break; }
                AfterCapture(ScreenCapture.Grab(r));
                break;
            case "Window":
                var w = ScreenCapture.GrabActiveWindow();
                if (w == null) { Notify("Can't take a screenshot of the active window"); break; }
                AfterCapture(w.Value.img);
                break;
            case "Library":
                LibraryWindow.ShowOrActivate();
                break;
            case "Scroll":
                SetCapturing(true);
                RegionSelector.Start((rect, img) =>
                    ScrollSession.Begin(rect, result => { SetCapturing(false); if (result != null) AfterCapture(result); }));
                break;
            case "Ocr":
                SetCapturing(true);
                RegionSelector.Start((rect, img) => RunOcr(img));
                break;
            case "AnyWindow":
                SetCapturing(true);
                RegionSelector.Start((rect, img) => AfterCapture(img), pickWindow: true);
                break;
            case "Clipboard":
                var clip = Output.ClipboardImage();
                if (clip == null) { Notify("Make sure your clipboard contains an image and try again"); break; }
                new EditorWindow(clip).Show();
                break;
        }
    }

    async void RunOcr(BitmapSource img)
    {
        var outcome = await Ocr.RecognizeAsync(img);
        var msg = Ocr.ApplyToClipboard(outcome);
        Log(msg);
        ToastWindow.Show(outcome.Kind == "qr" ? "QR code copied" : outcome.Kind == "text" ? "Text copied" : msg,
            outcome.Kind is "qr" or "text" ? msg.Substring(msg.IndexOf(':') + 1).Trim() : null, ok: outcome.Kind is "qr" or "text");
    }

    void AfterCapture(BitmapSource img)
    {
        var s = Settings.Current;
        bool copied = false, saved = false;
        string? savedPath = null, err = null;
        if (s.AfterGrabCopy) copied = Output.CopyToClipboard(img);
        if (s.AfterGrabSave) { savedPath = Output.SaveToFolder(img, out var e2); saved = savedPath != null; err = e2; }

        if (s.ShowMode == "Editor") { var ed = new EditorWindow(img); ed.Show(); ed.Activate(); return; }
        if (s.ShowMode == "Preview") { new PreviewWindow(img).Show(); return; }

        // "Nothing": confirm with a toast
        string dims = $"{img.PixelWidth} × {img.PixelHeight}";
        string folder = new DirectoryInfo(s.Folder).Name;
        if (copied) ToastWindow.Show("Copied to the clipboard", dims + (saved ? " · saved to " + folder : ""), img, ok: true);
        else if (saved) ToastWindow.Show("Saved to " + folder, dims, img, ok: true);
        else if (s.AfterGrabSave) ToastWindow.Show("Can't save the image", err);
        else ToastWindow.Show("Captured", dims, img);
    }
}

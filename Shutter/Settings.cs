using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Shutter;

public sealed class Settings
{
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shutter");
    public bool AfterGrabCopy { get; set; } = true;
    public bool AfterGrabSave { get; set; } = false;
    public string ShowMode { get; set; } = "Editor"; // Editor | Preview | Nothing
    public bool IndexText { get; set; } = true; // library: OCR saved screenshots so search finds text inside them
    public int ScrollMaxHeight { get; set; } = 20000; // scrolling capture cap (px)
    public bool RemoveBreaks { get; set; } = false; // OCR: join lines with spaces
    public string OcrLanguage { get; set; } = "auto";
    public BackdropSettings Backdrop { get; set; } = new();
    public string ColorFormat { get; set; } = "hex"; // hex | rgb | hsl
    public bool EscCopy { get; set; } = true;
    public bool EscSave { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public string Format { get; set; } = "auto"; // auto | png
    public Dictionary<string, string> Hotkeys { get; set; } = DefaultHotkeys();

    public static Dictionary<string, string> DefaultHotkeys() => new()
    {
        ["Area"] = "Ctrl+Shift+4",
        ["Fullscreen"] = "Ctrl+Shift+3",
        ["Delayed"] = "Ctrl+Shift+5",
        ["Repeat"] = "Ctrl+Shift+6",
        ["Library"] = "Ctrl+Shift+0",
        ["Scroll"] = "Ctrl+Shift+1",
        ["Ocr"] = "Ctrl+Shift+2",
        ["Window"] = "Ctrl+Shift+7",
        ["AnyWindow"] = "Ctrl+Shift+9",
        ["Clipboard"] = "Ctrl+Shift+8",
    };

    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shutter");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Current { get; private set; } = Load();

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    foreach (var kv in DefaultHotkeys())
                        if (!s.Hotkeys.ContainsKey(kv.Key)) s.Hotkeys[kv.Key] = kv.Value;
                    return s;
                }
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        ApplyStartup();
    }

    void ApplyStartup()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (StartWithWindows) key.SetValue("Shutter", "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue("Shutter", false);
        }
        catch { }
    }
}

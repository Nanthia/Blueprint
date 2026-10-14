using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace Shutter;

sealed class HotkeyService : IDisposable
{
    readonly HwndSource source;
    readonly Dictionary<int, string> ids = new();
    readonly Action<string> onAction;
    int nextId = 1;

    public HotkeyService(Action<string> onAction)
    {
        this.onAction = onAction;
        source = new HwndSource(new HwndSourceParameters("ShutterHotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        source.AddHook(Hook);
        PauseRequested += OnPause;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && ids.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            onAction(action);
        }
        return IntPtr.Zero;
    }

    /// <summary>Registers all hotkeys; returns names of those that could not be registered.</summary>
    public List<string> Register(Dictionary<string, string> map)
    {
        UnregisterAll();
        lastMap = map;
        var failed = new List<string>();
        foreach (var (name, text) in map)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!TryParse(text, out var mods, out var vk) || !Native.RegisterHotKey(source.Handle, nextId, mods | Native.MOD_NOREPEAT, vk))
            {
                failed.Add($"{name} ({text})");
                continue;
            }
            ids[nextId++] = name;
        }
        return failed;
    }

    /// <summary>Suspends/restores all registered hotkeys (used while the user records a new shortcut).</summary>
    public static event Action<bool>? PauseRequested;
    public static void Pause(bool paused) => PauseRequested?.Invoke(paused);

    Dictionary<string, string>? lastMap;

    void UnregisterAll()
    {
        foreach (var id in ids.Keys) Native.UnregisterHotKey(source.Handle, id);
        ids.Clear();
    }

    void OnPause(bool paused)
    {
        if (paused) UnregisterAll();
        else if (lastMap != null) Register(lastMap);
    }

    public void Dispose()
    {
        PauseRequested -= OnPause;
        UnregisterAll();
        source.Dispose();
    }

    static readonly Dictionary<Key, string> KeyNames = new()
    {
        [Key.Oem3] = "`", [Key.OemMinus] = "-", [Key.OemPlus] = "=", [Key.Oem4] = "[", [Key.Oem6] = "]", [Key.Oem5] = "\\",
        [Key.Oem1] = ";", [Key.Oem7] = "'", [Key.OemComma] = ",", [Key.OemPeriod] = ".", [Key.Oem2] = "/",
        [Key.Return] = "Enter", [Key.Prior] = "PageUp", [Key.Next] = "PageDown", [Key.Snapshot] = "PrintScreen",
    };

    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= Native.MOD_CONTROL; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default:
                    var friendly = KeyNames.FirstOrDefault(kv => kv.Value == part);
                    var name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                    Key key;
                    if (friendly.Value != null) key = friendly.Key;
                    else if (!Enum.TryParse<Key>(name, true, out key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    public static string Format(ModifierKeys m, Key key)
    {
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key >= Key.D0 && key <= Key.D9 ? ((int)(key - Key.D0)).ToString() : KeyNames.TryGetValue(key, out var nice) ? nice : key.ToString());
        return string.Join("+", parts);
    }
}

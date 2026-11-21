using System.Runtime.InteropServices;

namespace MarkItDownSharp.Desktop.Services;

/// <summary>
/// A notification-area icon for a WinUI window. WinUI has no built-in tray support, so this talks to
/// Shell_NotifyIcon directly and listens for the icon's messages by subclassing the window.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4;
    private const uint WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmNcDestroy = 0x0082;
    private const uint MfString = 0x0, MfSeparator = 0x800;
    private const uint TpmReturnCmd = 0x100, TpmRightButton = 0x2;
    private const uint ImageIcon = 1, LrLoadFromFile = 0x10, LrDefaultSize = 0x40;
    private const nuint SubclassId = 0x4D49445;
    private const int OpenCommand = 1, PauseCommand = 2, ExitCommand = 3;

    private readonly nint _window;
    private readonly SubclassProc _subclass;
    private readonly Action _open;
    private readonly Action _togglePause;
    private readonly Action _exit;
    private readonly Func<bool> _isPaused;
    private nint _icon;
    private bool _added;

    public TrayIcon(nint window, string iconPath, string tooltip, Action open, Action togglePause, Func<bool> isPaused, Action exit)
    {
        _window = window;
        _open = open;
        _togglePause = togglePause;
        _isPaused = isPaused;
        _exit = exit;
        _subclass = WindowProc;
        _icon = LoadImage(0, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        Tooltip = tooltip;
    }

    public string Tooltip { get; }

    public void Show()
    {
        if (_added)
        {
            return;
        }

        SetWindowSubclass(_window, _subclass, SubclassId, 0);
        var data = CreateData();
        _added = Shell_NotifyIcon(NimAdd, ref data);
    }

    public void SetTooltip(string text)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(text);
        Shell_NotifyIcon(NimModify, ref data);
    }

    public void Hide()
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData();
        Shell_NotifyIcon(NimDelete, ref data);
        RemoveWindowSubclass(_window, _subclass, SubclassId);
        _added = false;
    }

    public void Dispose()
    {
        Hide();
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private NotifyIconData CreateData(string? tip = null) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = NifMessage | NifIcon | NifTip,
        CallbackMessage = CallbackMessage,
        Icon = _icon,
        Tip = (tip ?? Tooltip).Length > 127 ? (tip ?? Tooltip)[..127] : tip ?? Tooltip
    };

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                    _open();
                    return 0;
                case WmRButtonUp:
                    ShowMenu();
                    return 0;
            }
        }
        else if (message == WmNcDestroy)
        {
            Hide();
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MfString, OpenCommand, "Open MarkItDownSharp");
            AppendMenu(menu, MfString, PauseCommand, _isPaused() ? "Resume monitoring" : "Pause monitoring");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, ExitCommand, "Exit");

            GetCursorPos(out var point);
            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton, point.X, point.Y, 0, _window, 0);
            switch (command)
            {
                case OpenCommand:
                    _open();
                    break;
                case PauseCommand:
                    _togglePause();
                    break;
                case ExitCommand:
                    _exit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
}

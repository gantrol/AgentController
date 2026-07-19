using System.Runtime.InteropServices;

namespace CodexController.Native;

internal static partial class Win32Input
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        // INPUT is a tagged union. Keeping MOUSEINPUT in the union is required
        // even for keyboard-only injection so Marshal.SizeOf<Input>() matches
        // Win32's native INPUT size (40 bytes on x64).
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(
        uint count,
        [In] Input[] inputs,
        int size);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    public static bool IsCodexForeground() =>
        CodexWindowActivator.IsForeground();

    public static bool IsProcessForeground(int expectedProcessId)
    {
        if (expectedProcessId <= 0)
        {
            return false;
        }

        var window = GetForegroundWindow();
        if (window == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        return processId == (uint)expectedProcessId;
    }

    public static bool FocusCodex() =>
        CodexWindowActivator.TryActivate();

    public static bool FocusCodexAndWait(int timeoutMs = 420)
    {
        if (IsCodexForeground())
        {
            return true;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        _ = FocusCodex();
        while (Environment.TickCount64 < deadline)
        {
            if (IsCodexForeground())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        _ = FocusCodex();
        return IsCodexForeground();
    }

    public static bool SendShortcut(string shortcut)
    {
        if (!TryParseShortcut(shortcut, out var modifiers, out var key))
        {
            return false;
        }

        var inputs = new List<Input>();
        foreach (var modifier in modifiers)
        {
            inputs.Add(KeyInput(modifier, keyUp: false));
        }

        inputs.Add(KeyInput(key, keyUp: false));
        inputs.Add(KeyInput(key, keyUp: true));

        for (var index = modifiers.Count - 1; index >= 0; index--)
        {
            inputs.Add(KeyInput(modifiers[index], keyUp: true));
        }

        return SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            Marshal.SizeOf<Input>()) == inputs.Count;
    }

    public static bool SendKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            KeyInput(virtualKey, keyUp: false),
            KeyInput(virtualKey, keyUp: true),
        };
        return SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Input>()) == inputs.Length;
    }

    public static bool SendText(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        var inputs = new Input[text.Length * 2];
        var index = 0;
        foreach (var character in text)
        {
            inputs[index++] = UnicodeInput(character, keyUp: false);
            inputs[index++] = UnicodeInput(character, keyUp: true);
        }

        return SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Input>()) == inputs.Length;
    }

    public static bool ClickAt(int x, int y)
    {
        if (!GetCursorPos(out var original))
        {
            return false;
        }

        try
        {
            if (!SetCursorPos(x, y))
            {
                return false;
            }

            var inputs = new[]
            {
                MouseButtonInput(MouseEventLeftDown),
                MouseButtonInput(MouseEventLeftUp),
            };
            return SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<Input>()) == inputs.Length;
        }
        finally
        {
            _ = SetCursorPos(original.X, original.Y);
        }
    }

    private static Input KeyInput(ushort key, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = GetKeyboardEventFlags(key, keyUp),
                },
            },
        };
    }

    private static Input UnicodeInput(char character, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = character,
                    Flags = KeyEventUnicode |
                        (keyUp ? KeyEventKeyUp : 0),
                },
            },
        };
    }

    private static Input MouseButtonInput(uint flags)
    {
        return new Input
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Flags = flags,
                },
            },
        };
    }

    internal static uint GetKeyboardEventFlags(
        ushort virtualKey,
        bool keyUp)
    {
        var flags = IsExtendedVirtualKey(virtualKey)
            ? KeyEventExtendedKey
            : 0;
        if (keyUp)
        {
            flags |= KeyEventKeyUp;
        }

        return flags;
    }

    private static bool IsExtendedVirtualKey(ushort virtualKey) =>
        virtualKey is
            0x21 or // Page Up
            0x22 or // Page Down
            0x23 or // End
            0x24 or // Home
            0x25 or // Left
            0x26 or // Up
            0x27 or // Right
            0x28 or // Down
            0x2D or // Insert
            0x2E or // Delete
            0x6F or // Numpad Divide
            0x90 or // Num Lock
            0xA3 or // Right Control
            0xA5;   // Right Alt

    private static bool TryParseShortcut(
        string shortcut,
        out List<ushort> modifiers,
        out ushort key)
    {
        modifiers = [];
        key = 0;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var parts = shortcut.Split(
            '+',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers.Add(0x11);
                    break;
                case "SHIFT":
                    modifiers.Add(0x10);
                    break;
                case "ALT":
                    modifiers.Add(0x12);
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers.Add(0x5B);
                    break;
                default:
                    if (!TryParseKey(part, out key))
                    {
                        return false;
                    }
                    break;
            }
        }

        return key != 0;
    }

    private static bool TryParseKey(string text, out ushort key)
    {
        var normalized = text.Trim().ToUpperInvariant();
        if (normalized.Length == 1)
        {
            var character = normalized[0];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = character;
                return true;
            }
        }

        if (
            normalized.StartsWith('F') &&
            int.TryParse(normalized[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            key = (ushort)(0x70 + functionNumber - 1);
            return true;
        }

        key = normalized switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "COMMA" => 0xBC,
            "PERIOD" => 0xBE,
            "SLASH" => 0xBF,
            "SEMICOLON" => 0xBA,
            "MINUS" => 0xBD,
            "PLUS" or "EQUALS" => 0xBB,
            "LBRACKET" or "[" => 0xDB,
            "RBRACKET" or "]" => 0xDD,
            _ => 0,
        };
        return key != 0;
    }
}

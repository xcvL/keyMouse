using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    // Virtual-key codes
    private const int Enter = 0x0D;
    private const int Left = 0x25;
    private const int Up = 0x26;
    private const int Right = 0x27;
    private const int Down = 0x28;
    private const int F12 = 0x7B;

    private const int LeftShift = 0xA0;
    private const int RightShift = 0xA1;
    private const int LeftCtrl = 0xA2;
    private const int RightCtrl = 0xA3;

    // Mouse input flags
    private const uint MouseMove = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MiddleDown = 0x0020;
    private const uint MiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;

    // INI settings
    private static int MoveStep = 6;
    private static int PollIntervalMs = 10;

    // Scroll settings
    private const int ScrollIntervalMs = 100;
    private const int ScrollDelta = 120;

    private static readonly bool[] PhysicalKeys = new bool[256];
    private static readonly bool[] BlockedKeys = new bool[256];

    private static readonly int[] ModifierKeys =
    {
        LeftCtrl, RightCtrl, LeftShift, RightShift
    };

    private enum ClickButton
    {
        Left,
        Right,
        Middle
    }

    private static readonly Queue<ClickButton> PendingClicks = new();

    // Keep a reference to prevent the delegate from being garbage-collected.
    private static readonly HookProc HookCallback = KeyboardHook;

    private static IntPtr hookHandle;
    private static bool exitRequested;
    private static int previousScrollDirection;
    private static long nextScrollTime;

    private static bool CtrlPressed =>
        PhysicalKeys[LeftCtrl] || PhysicalKeys[RightCtrl];

    private static bool ShiftPressed =>
        PhysicalKeys[LeftShift] || PhysicalKeys[RightShift];

    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Prevent multiple instances of this application from running.
        using var mutex = new Mutex(
            true,
            @"Local\KeyboardMouse_Hook_App",
            out bool firstInstance);

        if (!firstInstance)
            return;

        try
        {
            Run();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void Run()
    {
        LoadSettings();

        for (int key = 0; key < PhysicalKeys.Length; key++)
        {
            PhysicalKeys[key] =
                (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        hookHandle = SetWindowsHookEx(
            13, // WH_KEYBOARD_LL
            HookCallback,
            GetModuleHandle(null),
            0);

        if (hookHandle == IntPtr.Zero)
        {
            MessageBox.Show(
                new Win32Exception(
                    Marshal.GetLastWin32Error()).Message,
                "Failed to install the keyboard hook");

            return;
        }

        using var timer = new System.Windows.Forms.Timer
        {
            Interval = PollIntervalMs
        };

        timer.Tick += (_, _) =>
        {
            if (exitRequested)
            {
                Application.ExitThread();
                return;
            }

            try
            {
                ProcessMouse();
            }
            catch (Win32Exception ex)
            {
                timer.Stop();

                // Unblock keys before displaying the error dialog.
                RemoveHook();

                MessageBox.Show(
                    ex.Message,
                    "Mouse input failed");

                Application.ExitThread();
            }
        };

        try
        {
            timer.Start();

            // Run the message loop without a form.
            Application.Run();
        }
        finally
        {
            timer.Stop();
            RemoveHook();
        }
    }

    private static void RemoveHook()
    {
        if (hookHandle == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(hookHandle);
        hookHandle = IntPtr.Zero;
    }

    private static IntPtr KeyboardHook(
        int code,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (code < 0)
            return PassKey(code, wParam, lParam);

        var data =
            Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Do not treat input injected by this application or other programs
        // as physical key state changes.
        if ((data.flags & 0x10) != 0) // LLKHF_INJECTED
            return PassKey(code, wParam, lParam);

        int key = (int)data.vkCode;
        int message = wParam.ToInt32();

        bool isDown =
            message == 0x0100 || // WM_KEYDOWN
            message == 0x0104;   // WM_SYSKEYDOWN

        bool isUp =
            message == 0x0101 || // WM_KEYUP
            message == 0x0105;   // WM_SYSKEYUP

        if (key < 0 || key >= 256 || (!isDown && !isUp))
            return PassKey(code, wParam, lParam);

        bool wasDown = PhysicalKeys[key];
        PhysicalKeys[key] = isDown;

        // Exit when F12 is released. Block both key-down and key-up events.
        if (key == F12)
        {
            if (isUp)
                exitRequested = true;

            return (IntPtr)1;
        }

        if (isUp)
        {
            bool wasBlocked = BlockedKeys[key];
            BlockedKeys[key] = false;

            if (wasBlocked)
                return (IntPtr)1;

            return PassKey(code, wParam, lParam);
        }

        //  Also block auto-repeat events for blocked keys.
        if (BlockedKeys[key])
            return (IntPtr)1;

        // Do not intercept a key that was already pressed as normal input.
        if (wasDown)
            return PassKey(code, wParam, lParam);

        bool ctrl = CtrlPressed;
        bool shift = ShiftPressed;

        if (key == Enter && (ctrl || shift))
        {
            BlockedKeys[key] = true;

            if (ctrl && shift)
                PendingClicks.Enqueue(ClickButton.Right);
            else if (ctrl)
                PendingClicks.Enqueue(ClickButton.Left);
            else
                PendingClicks.Enqueue(ClickButton.Middle);

            return (IntPtr)1;
        }

        bool arrow =
            key == Left || key == Right ||
            key == Up || key == Down;

        bool verticalArrow = key == Up || key == Down;

        if ((ctrl && arrow) || (shift && verticalArrow))
        {
            BlockedKeys[key] = true;
            return (IntPtr)1;
        }

        return PassKey(code, wParam, lParam);
    }

    private static IntPtr PassKey(
        int code,
        IntPtr wParam,
        IntPtr lParam)
    {
        return CallNextHookEx(
            hookHandle, code, wParam, lParam);
    }

    private static bool IsMouseKeyHeld(int key)
    {
        return PhysicalKeys[key] && BlockedKeys[key];
    }

    private static void ProcessMouse()
    {
        while (PendingClicks.Count > 0)
        {
            switch (PendingClicks.Dequeue())
            {
                case ClickButton.Left:
                    Click(LeftDown, LeftUp);
                    break;

                case ClickButton.Right:
                    Click(RightDown, RightUp);
                    break;

                case ClickButton.Middle:
                    Click(MiddleDown, MiddleUp);
                    break;
            }
        }

        bool ctrl = CtrlPressed;
        bool shift = ShiftPressed;

        // Ctrl + arrow keys: move the mouse pointer.
        if (ctrl)
        {
            int x = (IsMouseKeyHeld(Right) ? 1 : 0)
                  - (IsMouseKeyHeld(Left) ? 1 : 0);

            int y = (IsMouseKeyHeld(Down) ? 1 : 0)
                  - (IsMouseKeyHeld(Up) ? 1 : 0);

            if (x != 0 || y != 0)
            {
                double scale = x != 0 && y != 0
                    ? MoveStep / Math.Sqrt(2)
                    : MoveStep;

                SendChecked(new[]
                {
                    MouseInput(
                        MouseMove,
                        dx: (int)Math.Round(x * scale),
                        dy: (int)Math.Round(y * scale))
                });
            }
        }

        // Ctrl + Shift + arrow keys only move the mouse pointer.
        int scrollDirection = shift && !ctrl
            ? (IsMouseKeyHeld(Up) ? 1 : 0)
              - (IsMouseKeyHeld(Down) ? 1 : 0)
            : 0;

        long now = Environment.TickCount64;

        if (scrollDirection != 0 &&
            (scrollDirection != previousScrollDirection ||
             now >= nextScrollTime))
        {
            SendWithoutModifiers(
                MouseInput(
                    MouseWheel,
                    wheelData: scrollDirection * ScrollDelta));

            nextScrollTime = now + ScrollIntervalMs;
        }

        previousScrollDirection = scrollDirection;
    }

    private static void Click(uint downFlag, uint upFlag)
    {
        SendWithoutModifiers(
            MouseInput(downFlag),
            MouseInput(upFlag));
    }

    private static void SendWithoutModifiers(
        params INPUT[] mouseInputs)
    {
        var batch = new List<INPUT>();
        var heldModifiers = new List<int>();

        // Temporarily release Ctrl/Shift.
        foreach (int key in ModifierKeys)
        {
            if (!PhysicalKeys[key])
                continue;

            heldModifiers.Add(key);
            batch.Add(KeyboardInput(key, keyUp: true));
        }

        batch.AddRange(mouseInputs);

        // Restore the Ctrl/Shift keys that were originally held down.
        foreach (int key in heldModifiers)
            batch.Add(KeyboardInput(key, keyUp: false));

        INPUT[] inputs = batch.ToArray();

        uint sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<INPUT>());

        if (sent == (uint)inputs.Length)
            return;

        int error = Marshal.GetLastWin32Error();

        // If only some inputs were sent, attempt to restore mouse button and modifier key states.
        if (sent > 0)
        {
            var recovery = new List<INPUT>();

            foreach (INPUT input in mouseInputs)
            {
                uint flags = input.data.mouse.dwFlags;

                if ((flags & LeftDown) != 0)
                    recovery.Add(MouseInput(LeftUp));

                if ((flags & RightDown) != 0)
                    recovery.Add(MouseInput(RightUp));

                if ((flags & MiddleDown) != 0)
                    recovery.Add(MouseInput(MiddleUp));
            }

            foreach (int key in heldModifiers)
                recovery.Add(KeyboardInput(key, keyUp: false));

            if (recovery.Count > 0)
            {
                INPUT[] restore = recovery.ToArray();

                SendInput(
                    (uint)restore.Length,
                    restore,
                    Marshal.SizeOf<INPUT>());
            }
        }

        throw new Win32Exception(
            error,
            "Failed to send mouse input. " +
            "Check the execution privileges of the target application and this program.");
    }

    private static void SendChecked(INPUT[] inputs)
    {
        uint sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<INPUT>());

        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to send mouse movement input.");
        }
    }

    private static INPUT MouseInput(
        uint flags,
        int dx = 0,
        int dy = 0,
        int wheelData = 0)
    {
        return new INPUT
        {
            type = 0, // INPUT_MOUSE
            data = new INPUTUNION
            {
                mouse = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = unchecked((uint)wheelData),
                    dwFlags = flags
                }
            }
        };
    }

    private static INPUT KeyboardInput(int key, bool keyUp)
    {
        // Right Ctrl is an extended key.
        uint flags = key == RightCtrl ? 0x0001u : 0u;

        if (keyUp)
            flags |= 0x0002; // KEYEVENTF_KEYUP

        return new INPUT
        {
            type = 1, // INPUT_KEYBOARD
            data = new INPUTUNION
            {
                keyboard = new KEYBDINPUT
                {
                    wVk = (ushort)key,
                    dwFlags = flags
                }
            }
        };
    }

    private static void LoadSettings()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "settings.ini");

        int moveStep = 6;
        int pollIntervalMs = 10;

        try
        {
            if (!File.Exists(path))
                return;

            bool inMouseSection = false;

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 ||
                    line.StartsWith(";") ||
                    line.StartsWith("#"))
                {
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string section =
                        line.Substring(1, line.Length - 2).Trim();

                    inMouseSection = section.Equals(
                        "Mouse",
                        StringComparison.OrdinalIgnoreCase);

                    continue;
                }

                if (!inMouseSection)
                    continue;

                int separator = line.IndexOf('=');

                if (separator < 0)
                    continue;

                string key =
                    line.Substring(0, separator).Trim();

                string valueText =
                    line.Substring(separator + 1).Trim();

                if (!int.TryParse(valueText, out int value) ||
                    value < 1 || value > 1000)
                {
                    continue;
                }

                if (key.Equals(
                    "MoveStep",
                    StringComparison.OrdinalIgnoreCase))
                {
                    moveStep = value;
                }
                else if (key.Equals(
                    "PollIntervalMs",
                    StringComparison.OrdinalIgnoreCase))
                {
                    pollIntervalMs = value;
                }
            }

            MoveStep = moveStep;
            PollIntervalMs = pollIntervalMs;
        }
        catch (IOException)
        {
            // Keep the default values.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the default values.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mouse;

        [FieldOffset(0)]
        public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint count,
        [In] INPUT[] inputs,
        int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
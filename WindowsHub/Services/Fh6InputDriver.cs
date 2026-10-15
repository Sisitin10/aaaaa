using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RealCam.WindowsHub.Services;

internal sealed class Fh6InputDriver
{
    private readonly HashSet<ushort> _heldKeys = [];
    private float? _lastYaw;
    private float? _lastPitch;
    private double? _lastZoom;

    public void ApplyPose(float x, float y, float z, float yaw, float pitch)
    {
        if (!IsForzaForeground()) { ReleaseKeys(); ResetDeltas(); return; }

        SetKey(0x44, x > 0.08f);  // D: move right
        SetKey(0x41, x < -0.08f); // A: move left
        SetKey(0x57, z < -0.08f); // W: move forward
        SetKey(0x53, z > 0.08f);  // S: move backward
        SetKey(0x45, y > 0.08f);  // E: move up
        SetKey(0x51, y < -0.08f); // Q: move down

        if (_lastYaw is float previousYaw && _lastPitch is float previousPitch)
        {
            var dx = Math.Clamp(WrapAngle(yaw - previousYaw) * 680, -300, 300);
            var dy = Math.Clamp((pitch - previousPitch) * -680, -300, 300);
            if (Math.Abs(dx) + Math.Abs(dy) >= 1) MoveMouse((int)dx, (int)dy);
        }
        _lastYaw = yaw;
        _lastPitch = pitch;
    }

    public void SetZoom(double value)
    {
        if (!IsForzaForeground()) { _lastZoom = value; return; }
        if (_lastZoom is double previous)
        {
            var wheel = (int)Math.Clamp((value - previous) * 1440, -480, 480);
            if (wheel != 0) MouseWheel(wheel);
        }
        _lastZoom = value;
    }

    public bool SendAction(string action)
    {
        if (action == "release") { ReleaseKeys(); ResetDeltas(); return true; }
        if (action == "recenter") { _lastYaw = null; _lastPitch = null; return true; }
        if (!IsForzaForeground()) return false;
        switch (action)
        {
            case "photoMode": TapKey(0x50); return true; // P
            case "reset": ReleaseKeys(); TapKey(0x52); ResetDeltas(); return true; // R
            default: return false;
        }
    }

    public void ReleaseKeys()
    {
        foreach (var key in _heldKeys.ToArray()) SendKey(key, keyUp: true);
        _heldKeys.Clear();
    }

    private void SetKey(ushort key, bool shouldHold)
    {
        if (shouldHold && _heldKeys.Add(key)) SendKey(key, keyUp: false);
        else if (!shouldHold && _heldKeys.Remove(key)) SendKey(key, keyUp: true);
    }

    private static void TapKey(ushort key)
    {
        SendKey(key, keyUp: false);
        SendKey(key, keyUp: true);
    }

    private static void MoveMouse(int dx, int dy) => SendMouse(dx, dy, 0, 0x0001);
    private static void MouseWheel(int delta) => SendMouse(0, 0, unchecked((uint)delta), 0x0800);

    private static void SendKey(ushort key, bool keyUp)
    {
        var input = new Input { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = keyUp ? 0x0002u : 0u } } };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private static void SendMouse(int dx, int dy, uint data, uint flags)
    {
        var input = new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags } } };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private void ResetDeltas() { _lastYaw = null; _lastPitch = null; _lastZoom = null; }

    private static float WrapAngle(float value)
    {
        while (value > MathF.PI) value -= 2 * MathF.PI;
        while (value < -MathF.PI) value += 2 * MathF.PI;
        return value;
    }

    private static bool IsForzaForeground()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return false;
        GetWindowThreadProcessId(handle, out var pid);
        try { return BridgeServer.IsForzaProcess(Process.GetProcessById((int)pid).ProcessName); }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public IntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

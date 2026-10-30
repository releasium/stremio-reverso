using System.Runtime.InteropServices;
using StremioReverso.Win32;

namespace StremioReverso.Stremio;

/// Stremio's player toggles play/pause on Space.
internal static class Playback
{
    public static void TogglePlayPause()
    {
        var inputs = new[] { Key(0), Key(Native.KEYEVENTF_KEYUP) };
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    static Native.INPUT Key(uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = Native.VK_SPACE, dwFlags = flags } },
    };
}

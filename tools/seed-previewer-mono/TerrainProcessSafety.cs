using System;
using System.Runtime.InteropServices;

// Child-process diagnostics only. A native failure still returns its OS failure code.
static class TerrainProcessSafety
{
    [DllImport("kernel32.dll")] static extern uint GetErrorMode();
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    public static void DisableCrashDialogs()
    {
        if(Environment.OSVersion.Platform==PlatformID.Win32NT)SetErrorMode(GetErrorMode()|0x0001u|0x0002u);
    }
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class WinFinder {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<string> Find(uint pid) {
        var list = new List<string>();
        EnumWindows((hWnd, lParam) => {
            uint procId;
            GetWindowThreadProcessId(hWnd, out procId);
            if (procId == pid) {
                bool vis = IsWindowVisible(hWnd);
                RECT r;
                GetWindowRect(hWnd, out r);
                list.Add("hWnd=0x" + hWnd.ToInt64().ToString("X") + " vis=" + vis + " rect=" + r.Left + "," + r.Top + ".." + r.Right + "," + r.Bottom);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
'@

[WinFinder]::Find(38256)

param([string[]]$Names = @('SuperD3Hud','Diablo III64'))
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WinEnum {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(CB c, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public struct R { public int L, T, Rt, B; }
  public static List<string> Get(uint pid) {
    var l = new List<string>();
    EnumWindows((h, x) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid) {
        R r; GetWindowRect(h, out r);
        var s = new StringBuilder(100); GetWindowText(h, s, 100);
        l.Add(h + " vis=" + IsWindowVisible(h) + " rect=" + r.L + "," + r.T + "," + r.Rt + "," + r.B + " ex=" + GetWindowLong(h, -20).ToString("X") + " '" + s + "'");
      }
      return true;
    }, IntPtr.Zero);
    return l;
  }
  public static string Fg() { return GetForegroundWindow().ToString(); }
}
"@
foreach ($n in $Names) {
  foreach ($p in Get-Process $n -ErrorAction SilentlyContinue) {
    "== $($p.ProcessName) pid=$($p.Id)"
    [WinEnum]::Get([uint32]$p.Id)
  }
}
"foreground hwnd = $([WinEnum]::Fg())"

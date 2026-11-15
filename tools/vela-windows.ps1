# Lists every window owned by Vela.exe with its rect, clip region and styles.
# Note: this PowerShell is not DPI-aware, so numbers are scaled by 1/(display scale).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/vela-windows.ps1
param([string]$Name = "Vela")
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowRgnBox(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
  public struct R { public int L, T, Ri, B; }
}
"@
$global:out = New-Object System.Collections.ArrayList
$ids = (Get-Process $Name -ErrorAction SilentlyContinue).Id
[W]::EnumWindows({ param($h, $l)
  $pid2 = 0; [W]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
  if ($ids -contains $pid2) {
    $r = New-Object W+R; [W]::GetWindowRect($h, [ref]$r) | Out-Null
    $sb = New-Object Text.StringBuilder 256; [W]::GetWindowText($h, $sb, 256) | Out-Null
    $rg = New-Object W+R; $type = [W]::GetWindowRgnBox($h, [ref]$rg)
    $st = [W]::GetWindowLongPtrW($h, -16).ToInt64().ToString("X"); $ex = [W]::GetWindowLongPtrW($h, -20).ToInt64().ToString("X")
    [void]$global:out.Add(("{0} vis={1} '{2}' rect=[{3},{4},{5},{6}] rgnType={7} rgn=[{8},{9},{10},{11}] style={12} ex={13}" -f $h, [W]::IsWindowVisible($h), $sb, $r.L, $r.T, $r.Ri, $r.B, $type, $rg.L, $rg.T, $rg.Ri, $rg.B, $st, $ex))
  }
  $true }, [IntPtr]::Zero) | Out-Null
$global:out

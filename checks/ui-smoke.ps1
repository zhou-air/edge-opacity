param([switch]$InspectOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SmokeNative {
 [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
 [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
}
'@
$appProcess = Get-Process EdgeOpacity -ErrorAction SilentlyContinue | Select-Object -First 1
if (!$appProcess) { throw 'App not running' }
$root = $null
for ($i = 0; $i -lt 40; $i++) {
    $appProcess.Refresh()
    if ($appProcess.HasExited) { throw 'App exited during startup' }
    if ($appProcess.MainWindowHandle -ne 0) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
        break
    }
    Start-Sleep -Milliseconds 250
}
if (!$root) { throw 'No application window' }
function Find-Control([string]$id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $control = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$control) { throw "Missing control $id" }
    return $control
}
function Check([bool]$passed, [string]$label) {
    if (!$passed) { throw "FAIL: $label" }
    Write-Output "PASS: $label"
}
function Invoke-Control([string]$id) {
    $control = Find-Control $id
    $pattern = $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 200
}
$slider = Find-Control 'OpacitySlider'
$picker = Find-Control 'WindowPicker'
Check ($null -ne $slider -and $null -ne $picker) 'WinUI window and main controls available'
$range = [System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
Check ($range.Current.Minimum -eq 5 -and $range.Current.Maximum -eq 100) 'UI slider range is 5 to 100'
if ($InspectOnly) {
    Write-Output "Slider enabled: $($slider.Current.IsEnabled)"
    exit 0
}
$edgeProcess = Get-Process msedge -ErrorAction SilentlyContinue | Where-Object MainWindowTitle -like '*EdgeOpacity Functional Check*' | Select-Object -First 1
if (!$edgeProcess) { throw 'Isolated Edge test window is missing' }
$edgeHwnd = $edgeProcess.MainWindowHandle
$expand = [System.Windows.Automation.ExpandCollapsePattern]$picker.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
$expand.Expand()
Start-Sleep -Milliseconds 300
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $edgeProcess.MainWindowTitle)
$item = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
if ($item -and $item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
    ([System.Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
} else {
    $items = $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
    $chosen = $false
    foreach ($candidate in $items) {
        if ($candidate.Current.Name -like '*EdgeOpacity Functional Check*') {
            ([System.Windows.Automation.SelectionItemPattern]$candidate.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
            $chosen = $true
            break
        }
    }
    if (!$chosen) { throw 'Cannot select isolated Edge window' }
}
$expand.Collapse()
Start-Sleep -Milliseconds 200
function Read-Alpha {
    [uint32]$key = 0; [byte]$alpha = 0; [uint32]$flags = 0
    if (![SmokeNative]::GetLayeredWindowAttributes($edgeHwnd, [ref]$key, [ref]$alpha, [ref]$flags)) { return 255 }
    return [int]$alpha
}
$initialStyle = [SmokeNative]::GetWindowLongPtr($edgeHwnd, -20).ToInt64()
try {
    $range.SetValue(5)
    Start-Sleep -Milliseconds 300
    Check ((Read-Alpha) -eq 13) 'UI slider applies 5 percent to real Edge window'
    $preset = Find-Control 'Preset60'
    ([System.Windows.Automation.TogglePattern]$preset.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 300
    Check ((Read-Alpha) -eq 153) 'UI preset applies 60 percent to real Edge window'
    Invoke-Control 'RestoreSelected'
    Check ([SmokeNative]::GetWindowLongPtr($edgeHwnd, -20).ToInt64() -eq $initialStyle) 'restore current returns original Edge style'
    $range.SetValue(20)
    Start-Sleep -Milliseconds 200
    Invoke-Control 'RestoreAll'
    Check ([SmokeNative]::GetWindowLongPtr($edgeHwnd, -20).ToInt64() -eq $initialStyle) 'restore all returns original Edge style'
    $range.SetValue(5)
    Start-Sleep -Milliseconds 200
    $null = [SmokeNative]::PostMessage($appProcess.MainWindowHandle, 0x0312, [IntPtr]0xED6, [IntPtr]0)
    Start-Sleep -Milliseconds 300
    Check ([SmokeNative]::GetWindowLongPtr($edgeHwnd, -20).ToInt64() -eq $initialStyle) 'global hotkey handler restores Edge'
    $range.SetValue(40)
    Start-Sleep -Milliseconds 200
    $null = $appProcess.CloseMainWindow()
    Check ($appProcess.WaitForExit(8000)) 'application closes normally'
    Check ([SmokeNative]::GetWindowLongPtr($edgeHwnd, -20).ToInt64() -eq $initialStyle) 'closing application restores Edge'
    Write-Output 'SUCCESS: real WinUI / Edge integration checks complete'
} finally {
    if (!$appProcess.HasExited) {
        try { Invoke-Control 'RestoreAll' } catch { Write-Warning $_ }
        $null = $appProcess.CloseMainWindow()
        $null = $appProcess.WaitForExit(5000)
    }
    # This exact handle belongs to the isolated test profile, not an existing user window.
    $null = [SmokeNative]::PostMessage($edgeHwnd, 0x0010, [IntPtr]0, [IntPtr]0)
}

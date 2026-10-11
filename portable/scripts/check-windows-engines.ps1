param([Parameter(Mandatory=$true)][string]$Directory, [switch]$UiOnly)
$ErrorActionPreference = 'Stop'
$out = Join-Path $Directory 'results'
New-Item -ItemType Directory -Force $out | Out-Null
Start-Transcript -Path (Join-Path $out 'checks.txt') -Force | Out-Null
$app = $null
try {
    if (-not $UiOnly) {
    $env:DICTATE_SPEECH_FIXTURES = Join-Path $Directory 'fixtures'
    & (Join-Path $Directory 'native/Dictate.NativeChecks.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Native speech and field checks failed.' }
    foreach ($manifest in @('samples', 'german-samples')) {
        & (Join-Path $Directory 'speech/Dictate.Checks.exe') --evaluate (Join-Path $Directory "fixtures/$manifest.json") (Join-Path $Directory 'fixtures/models') (Join-Path $out "$manifest.json")
        if ($LASTEXITCODE -ne 0) { throw "The $manifest evaluation failed." }
    }
    }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $app = Start-Process (Join-Path $Directory 'app/Dictate.Desktop.exe') -PassThru
    for ($i=0; $i -lt 100; $i++) {
        Start-Sleep -Milliseconds 100
        $app.Refresh()
        if ($app.HasExited) { throw 'Dictate exited during startup.' }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $settings = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Settings')))
    $settings.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
    $selector = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ComboBox)))
    if ($null -eq $selector) { throw 'The speech engine selector is missing.' }
    function Selected($control) {
        $pattern = $control.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
        return $pattern.Current.GetSelection()[0].Current.Name
    }
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class DictateEngineKeys { [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra); }
'@
    function Choose($control, $name) {
        if ((Selected $control) -eq $name) { return }
        $control.SetFocus()
        $key = if ($name -eq 'Whistle') { [byte]0x28 } else { [byte]0x26 }
        [DictateEngineKeys]::keybd_event($key,0,0,[UIntPtr]::Zero)
        [DictateEngineKeys]::keybd_event($key,0,2,[UIntPtr]::Zero)
        Start-Sleep -Milliseconds 500
    }
    $original = Selected $selector
    try {
        Choose $selector 'Whistle'
        Stop-Process -Id $app.Id
        $app.WaitForExit()
        $app = Start-Process (Join-Path $Directory 'app/Dictate.Desktop.exe') -PassThru
        for ($i=0; $i -lt 100; $i++) { Start-Sleep -Milliseconds 100; $app.Refresh(); if ($app.MainWindowHandle -ne [IntPtr]::Zero) { break } }
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
        $settings = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Settings')))
        $settings.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 300
        $selector = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Speech engine')),
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ComboBox)))))
        if ((Selected $selector) -ne 'Whistle') { throw 'The engine selection did not survive restart.' }
        Write-Output 'Windows Whistle selection survived restart.'
    }
    finally { if ($null -ne $selector -and -not $app.HasExited) { Choose $selector $original } }
    Write-Output 'Windows Settings engine selection and restart persistence passed.'
    Set-Content (Join-Path $out 'result.txt') 'passed'
}
catch {
    Write-Output $_
    Set-Content (Join-Path $out 'result.txt') ('failed: ' + $_)
    exit 1
}
finally {
    if ($null -ne $app -and -not $app.HasExited) { Stop-Process -Id $app.Id }
    Stop-Transcript | Out-Null
}

param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$FixtureAudio,
    [Parameter(Mandatory = $true)][string]$ModelDirectory
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$app = $null
$shortcutFixture = $null
$result = 1
Start-Transcript -Path (Join-Path $OutputDirectory 'checks.txt') -Force | Out-Null
try {
    & (Join-Path $PackageDirectory 'native/Dictate.NativeChecks.exe')
    if ($LASTEXITCODE -ne 0) { throw "Windows native checks failed: $LASTEXITCODE" }
    & (Join-Path $PackageDirectory 'speech/Dictate.Checks.exe') $FixtureAudio $ModelDirectory
    if ($LASTEXITCODE -ne 0) { throw "Windows CPU checks failed: $LASTEXITCODE" }
    $previousIntrinsics = [Environment]::GetEnvironmentVariable('DOTNET_EnableHWIntrinsic', 'Process')
    try {
        $env:DOTNET_EnableHWIntrinsic = '0'
        & (Join-Path $PackageDirectory 'speech/Dictate.Checks.exe') $FixtureAudio $ModelDirectory
        if ($LASTEXITCODE -ne 0) { throw "Windows no-AVX checks failed: $LASTEXITCODE" }
        Write-Output 'Windows no-AVX CPU recognition, silence, cancellation, and recovery passed.'
    }
    finally { [Environment]::SetEnvironmentVariable('DOTNET_EnableHWIntrinsic', $previousIntrinsics, 'Process') }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DictateCheckWindow {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr device, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
    $app = Start-Process (Join-Path $PackageDirectory 'app/Dictate.Desktop.exe') -PassThru
    for ($i = 0; $i -lt 100; $i++) {
        Start-Sleep -Milliseconds 100
        $app.Refresh()
        if ($app.HasExited) { throw 'The Windows app exited during startup.' }
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    if ($app.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The Windows app did not open a window.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    foreach ($page in @('Home', 'History', 'Settings')) {
        $condition = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $page))
        )
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $button) { throw "The Windows app did not expose the $page button." }
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 500
        if ($window.Current.IsOffscreen) { throw 'The app window is not visible.' }
        $rectangle = $window.Current.BoundingRectangle
        $bitmap = New-Object System.Drawing.Bitmap([int]$rectangle.Width, [int]$rectangle.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $device = $graphics.GetHdc()
            try {
                if (-not [DictateCheckWindow]::PrintWindow($app.MainWindowHandle, $device, 2)) { throw 'The app window could not be captured.' }
            }
            finally { $graphics.ReleaseHdc($device) }
            $bitmap.Save((Join-Path $OutputDirectory ($page.ToLowerInvariant() + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
        Write-Output "Windows software-rendered $page page opened."
    }
    $hashes = Get-ChildItem $OutputDirectory -Filter '*.png' | Get-FileHash -Algorithm SHA256
    if (($hashes.Hash | Select-Object -Unique).Count -ne 3) { throw 'The Windows pages produced identical screenshots.' }
    $hashes | Format-Table Hash, Path
    $homeButton = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Home')))
    $homeButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $command = Join-Path $OutputDirectory ('shortcut-' + [Guid]::NewGuid().ToString())
    $shortcutFixture = Start-Process (Join-Path $PackageDirectory 'native/Dictate.NativeChecks.exe') -ArgumentList ('--fixture "' + $command + '"') -PassThru
    for ($i = 0; $i -lt 100 -and -not (Test-Path ($command + '.ready')); $i++) { Start-Sleep -Milliseconds 50 }
    if (-not (Test-Path ($command + '.ready'))) { throw 'The shortcut target did not open.' }
    Set-Content -Path $command -Value 'reset' -NoNewline
    for ($i = 0; $i -lt 100 -and (Test-Path $command); $i++) { Start-Sleep -Milliseconds 50 }
    if ((Test-Path $command) -or (Test-Path ($command + '.error'))) { throw 'The shortcut target did not receive focus.' }
    try {
        [DictateCheckWindow]::keybd_event(0x77, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 700
        $stop = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Stop dictation')))
        if ($null -eq $stop) { throw 'F8 did not start recording in the app.' }
        [DictateCheckWindow]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero)
        [DictateCheckWindow]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
    }
    finally {
        [DictateCheckWindow]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
        [DictateCheckWindow]::keybd_event(0x77, 0, 2, [UIntPtr]::Zero)
    }
    Start-Sleep -Milliseconds 500
    $start = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Start dictation')))
    if ($null -eq $start) { throw 'Escape did not cancel recording in the app.' }
    Write-Output 'Windows F8 recording and Escape cancellation passed with an external field focused.'
    $result = 0
}
catch { Write-Output $_.ToString() }
finally {
    if ($null -ne $shortcutFixture -and -not $shortcutFixture.HasExited) { Stop-Process -Id $shortcutFixture.Id }
    if ($null -ne $app -and -not $app.HasExited) { Stop-Process -Id $app.Id }
    Stop-Transcript | Out-Null
    Set-Content -Path (Join-Path $OutputDirectory 'exit-code.txt') -Value $result
}
exit $result

param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$NativeChecksDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$FixtureDirectory,
    [string]$SeedScript = (Join-Path $PSScriptRoot '../../desktop/scripts/seed-checks.cjs')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$result = 1
$app = $null
Start-Transcript -Path (Join-Path $OutputDirectory 'checks.txt') -Force | Out-Null
try {
    $env:DICTATE_SPEECH_FIXTURES = $FixtureDirectory
    & (Join-Path $NativeChecksDirectory 'Dictate.NativeChecks.exe')
    if ($LASTEXITCODE -ne 0) { throw "Windows native checks failed: $LASTEXITCODE" }
    $data = Join-Path $OutputDirectory 'app-data'
    & node $SeedScript $data
    if ($LASTEXITCODE -ne 0) { throw 'The isolated app data could not be prepared.' }
    # Native recognition checks and the Electron service use the same CPU models.
    Copy-Item (Join-Path $FixtureDirectory 'models') (Join-Path $data 'models') -Recurse -Force
    $executable = Join-Path $PackageDirectory 'Dictate.exe'
    foreach ($engine in @('', 'whistle', 'whisper')) {
        $name = if ($engine) { "restart-$engine" } else { 'ui' }
        $destination = Join-Path $OutputDirectory $name
        $arguments = '--data-directory "' + $data + '" --verify-desktop "' + $destination + '"'
        if ($engine) { $arguments += ' --verify-restart ' + $engine }
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $executable
        $start.Arguments = $arguments
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $app = [System.Diagnostics.Process]::Start($start)
        $stdout = $app.StandardOutput.ReadToEndAsync()
        $stderr = $app.StandardError.ReadToEndAsync()
        if (-not $app.WaitForExit(180000)) { throw "The $name app check did not exit." }
        [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "$name.stdout.txt"), $stdout.GetAwaiter().GetResult())
        [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "$name.stderr.txt"), $stderr.GetAwaiter().GetResult())
        if ($app.ExitCode -ne 0 -or (Get-Content (Join-Path $destination 'exit-code.txt')).Trim() -ne '0') {
            if (Test-Path (Join-Path $destination 'error.txt')) { Get-Content (Join-Path $destination 'error.txt') }
            throw "The $name app check failed."
        }
        Get-Content (Join-Path $destination 'checks.txt')
    }
    $result = 0
}
catch { Write-Output $_.ToString() }
finally {
    if ($null -ne $app -and -not $app.HasExited) { Stop-Process -Id $app.Id }
    Stop-Transcript | Out-Null
    Set-Content -Path (Join-Path $OutputDirectory 'exit-code.txt') -Value $result
}
exit $result

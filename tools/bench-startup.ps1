#Requires -Version 5.1
<#
.SYNOPSIS
    Measures OpenCleaner.UI startup to first visible main window.

.DESCRIPTION
    Launches a built OpenCleaner.UI.exe repeatedly, measures process start until
    MainWindowHandle becomes visible, records working-set metrics, then asks the
    window to close gracefully. Results are written to CSV.

    This script only launches the application. It does not install, clean, change
    the registry, or modify the network configuration.

.EXAMPLE
    .\tools\bench-startup.ps1 -Iterations 10
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [ValidateRange(1, 100)]
    [int]$Iterations = 5,
    [string]$OutputPath,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 60,
    [ValidateRange(0, 10000)]
    [int]$SettleMilliseconds = 500
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ExePath) {
    $candidates = @(
        (Join-Path $repoRoot 'src\OpenCleaner.UI\bin\Release\net10.0-windows\OpenCleaner.UI.exe'),
        (Join-Path $repoRoot 'src\OpenCleaner.UI\bin\Debug\net10.0-windows\OpenCleaner.UI.exe')
    )
    $ExePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath)) {
    throw "OpenCleaner.UI.exe not found. Build first or pass -ExePath."
}
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'artifacts\startup-bench.csv'
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$running = Get-Process -Name 'OpenCleaner.UI' -ErrorAction SilentlyContinue
if ($running) {
    throw "OpenCleaner.UI is already running (pid $($running.Id -join ', ')). Close it before benchmarking; the single-instance mutex would make every run exit immediately."
}

Write-Host "Benchmarking $ExePath ($Iterations run(s))..."

$rows = New-Object System.Collections.Generic.List[object]
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $ExePath
    $startInfo.WorkingDirectory = Split-Path -Parent $ExePath
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = ''

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $visible = $false
    $startupMs = $null
    $workingSet = $null
    $privateMemory = $null
    $peakWorkingSet = $null
    $forcedClose = $false
    $exitCode = $null

    try {
        try { [void]$process.WaitForInputIdle(1000) } catch { }

        $deadline = $stopwatch.Elapsed.TotalSeconds + $TimeoutSeconds
        while (-not $process.HasExited -and $stopwatch.Elapsed.TotalSeconds -lt $deadline) {
            $process.Refresh()
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                $visible = $true
                break
            }
            Start-Sleep -Milliseconds 25
        }

        $process.Refresh()
        if ($visible) {
            $startupMs = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
            if ($SettleMilliseconds -gt 0) { Start-Sleep -Milliseconds $SettleMilliseconds }
            $process.Refresh()
        }
        if (-not $process.HasExited) {
            $workingSet = $process.WorkingSet64
            $privateMemory = $process.PrivateMemorySize64
            $peakWorkingSet = $process.PeakWorkingSet64
        }

        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(10000)) {
                try { $process.Kill(); $forcedClose = $true } catch { }
                [void]$process.WaitForExit(5000)
            }
        }
        $exitCode = if ($process.HasExited) { $process.ExitCode } else { $null }
    }
    finally {
        if (-not $process.HasExited) {
            try { $process.Kill() } catch { }
            [void]$process.WaitForExit(5000)
        }
        $stopwatch.Stop()
        $process.Dispose()
    }

    [pscustomobject]@{
        Iteration            = $iteration
        StartedAtUtc         = (Get-Date).ToUniversalTime().ToString('o')
        Visible              = $visible
        StartupMs            = $startupMs
        WorkingSet64         = $workingSet
        PrivateMemorySize64  = $privateMemory
        PeakWorkingSet64     = $peakWorkingSet
        ForcedClose          = $forcedClose
        ExitCode             = $exitCode
    } | ForEach-Object { $rows.Add($_) }

    if ($visible) {
        Write-Host ("Run {0}/{1}: {2} ms, working set {3:N0} KB" -f $iteration, $Iterations,
            $startupMs, [Math]::Round($workingSet / 1KB))
    }
    else {
        Write-Host ("Run {0}/{1}: window not visible before timeout" -f $iteration, $Iterations) -ForegroundColor Yellow
    }
}

$rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8

$visibleRuns = @($rows | Where-Object { $_.Visible -and $null -ne $_.StartupMs })
if ($visibleRuns.Count -gt 0) {
    $average = ($visibleRuns | Measure-Object -Property StartupMs -Average).Average
    $minimum = ($visibleRuns | Measure-Object -Property StartupMs -Minimum).Minimum
    $maximum = ($visibleRuns | Measure-Object -Property StartupMs -Maximum).Maximum
    Write-Host ("Startup ms: min {0:N1}, avg {1:N1}, max {2:N1} over {3} visible run(s)." -f
        $minimum, $average, $maximum, $visibleRuns.Count)
}
Write-Host "CSV written to $OutputPath"

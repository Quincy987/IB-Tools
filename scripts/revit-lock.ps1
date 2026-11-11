<#
.SYNOPSIS
    Revit test slot for IB Tools: sessions on this machine test in Revit one at a time.

.DESCRIPTION
    Lock file: %LOCALAPPDATA%\IBTools\revit-test.lock, one line "<owner> <UTC time>".
    A lock younger than 2 hours held by someone else means: do not deploy, do not start
    Revit, come back later. An older lock is stale and may be taken over.

    Actions:
      status   show the lock and the Revit this tooling started (exit 2 if busy by someone else)
      take     take or refresh the lock for <owner>            (exit 2 if busy by someone else)
      release  close OUR test Revit (unless -KeepRevit), then remove the lock if it is ours
      stop     close OUR test Revit only, keep the lock

    "Our Revit" = the process recorded in %LOCALAPPDATA%\IBTools\revit.pid by build.ps1,
    started by the same owner. No other Revit process is ever touched.

    Owner: -Owner, else $env:IBTOOLS_LOCK_OWNER, else "<git branch>@<repo folder>".

.EXAMPLE
    .\scripts\revit-lock.ps1 status
    .\scripts\revit-lock.ps1 take
    .\scripts\revit-lock.ps1 release
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet("status", "take", "release", "stop")]
    [string]$Action = "status",

    [string]$Owner,

    # take: take over a fresh lock of someone else; release: remove someone else's lock.
    [switch]$Force,

    # release: leave our Revit running (only remove the lock).
    [switch]$KeepRevit
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "ibtools-common.ps1")

if (-not $Owner) { $Owner = Get-IBToolsOwner }
$Owner = $Owner -replace '\s+', '_'

function Show-OwnRevit {
    $rec = Get-IBToolsRevitRecord
    $proc = Get-IBToolsRevitProcess
    if ($proc) {
        Write-Host ("Test-Revit: pid {0}, Revit {1}, gestart door {2}" -f $rec.Pid, $rec.Version, $rec.Owner)
    } else {
        Write-Host "Test-Revit: geen (door deze scripts gestarte Revit draait niet)"
    }
}

switch ($Action) {
    "status" {
        $lock = Get-IBToolsLock
        Write-Host ("Slot: {0}" -f (Format-IBToolsLock $lock))
        Write-Host ("Ik:   {0}" -f $Owner)
        Show-OwnRevit
        if ($lock -and -not $lock.IsStale -and $lock.Owner -ne $Owner) { exit 2 }
        exit 0
    }
    "take" {
        $result = Enter-IBToolsLock -Owner $Owner -Force:$Force
        if ($result -eq "busy") {
            Write-Host ("BEZET: {0}. Niet deployen/Revit starten; probeer het over ~10 min opnieuw." -f (Format-IBToolsLock (Get-IBToolsLock))) -ForegroundColor Yellow
            exit 2
        }
        Write-Host ("Slot {0} door {1}." -f $(if ($result -eq "refreshed") { "ververst" } else { "genomen" }), $Owner) -ForegroundColor Green
        exit 0
    }
    "stop" {
        Write-Host (Stop-IBToolsRevit -Owner $Owner)
        exit 0
    }
    "release" {
        $exitCode = 0
        try {
            if (-not $KeepRevit) {
                Write-Host (Stop-IBToolsRevit -Owner $Owner -AnyOwner:$Force)
            }
        } catch {
            Write-Host ("Afsluiten eigen Revit mislukt: {0}" -f $_.Exception.Message) -ForegroundColor Red
            $exitCode = 1
        } finally {
            if (Exit-IBToolsLock -Owner $Owner -Force:$Force) {
                Write-Host "Slot vrijgegeven." -ForegroundColor Green
            } else {
                Write-Host ("Slot NIET vrijgegeven: {0} (gebruik -Force als dat echt van jou is)." -f (Format-IBToolsLock (Get-IBToolsLock))) -ForegroundColor Yellow
                $exitCode = 2
            }
        }
        exit $exitCode
    }
}

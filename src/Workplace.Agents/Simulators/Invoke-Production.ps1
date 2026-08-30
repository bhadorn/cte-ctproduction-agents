#Requires -Version 5.1
<#
.SYNOPSIS
  Drives a workplace simulator through a production run by rewriting its JSON file.

.DESCRIPTION
  The workplace agent watches these simulator files and reads them back on every change, so
  writing the file is how a simulated machine reports. This script produces a given number of
  parts at a given cycle time, with a stop of a few seconds between two parts - real production
  is never perfectly steady, and the stops are what give the OEE calculation something to
  separate production time from unplanned interruption.

  What the agent makes of the fields (SimulatorControl_v1 / MachineStateProcessor, first rule
  that matches wins):

      AlarmState != 0                            -> MachineState 1   Disturbance
      OperationMode != 1                         -> MachineState 100 Setup
      OperationMode == 1 and RunningState == 3   -> MachineState 200 Production
      OperationMode == 1 and RunningState == 2   -> MachineState 300 Production Stopped
      OperationMode == 1 and RunningState == 1   -> MachineState 310 Stopped: Missing Material
      OperationMode == 1                         -> MachineState 320 Stopped: Missing Personal

  PartCounter is what PerformanceProcessor turns into ProducedQuantity, one part per increment,
  and the cycle time it measures is the wall-clock gap between two increments - so the run has to
  take real time, it cannot be faked by writing the counter in a loop.

  Every other field in the file is left exactly as it was.

.PARAMETER File
  Name of the simulator file, resolved next to this script. Example: Simulator1.json

.PARAMETER Parts
  Number of parts to produce.

.PARAMETER CycleTimeSeconds
  Seconds of production per part. Set the job's SetCycleTime to this to get a performance
  factor near 1, or lower to see the factor drop.

.PARAMETER MinStopSeconds
  Shortest stop between two parts. Default 4.

.PARAMETER MaxStopSeconds
  Longest stop between two parts. Default 10.

.EXAMPLE
  .\Invoke-Production.ps1 -File Simulator1.json -Parts 20 -CycleTimeSeconds 15

.EXAMPLE
  .\Invoke-Production.ps1 -File Simulator1.json -Parts 5 -CycleTimeSeconds 30 -MinStopSeconds 2 -MaxStopSeconds 5

.NOTES
  Start the job on the workplace in the platform first - the agent needs a job to attribute the
  parts to. Stop or pause it in the platform afterwards; this script deliberately does not touch
  the job, only the machine.

  The run rewrites the simulator file many times, so it will show up as a local modification.

  A stop only shows up as a production stop on the platform if it outlasts the workplace's
  MaxChangeOverTime - a longer one is taken for a change-over. A workplace left at 01:00:00 swallows
  every stop this script makes.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $File,

  [Parameter(Mandatory = $true)]
  [ValidateRange(1, 100000)]
  [int] $Parts,

  [Parameter(Mandatory = $true)]
  [ValidateRange(0.1, 3600)]
  [double] $CycleTimeSeconds,

  [ValidateRange(0, 3600)]
  [double] $MinStopSeconds = 4,

  [ValidateRange(0, 3600)]
  [double] $MaxStopSeconds = 10
)

$ErrorActionPreference = 'Stop'

if ($MinStopSeconds -gt $MaxStopSeconds)
{
  throw "MinStopSeconds ($MinStopSeconds) is larger than MaxStopSeconds ($MaxStopSeconds)."
}

#Running states of the simulator, as MachineStateProcessor reads them
$RUNNING_Production = 3
$RUNNING_Stopped    = 2
$MODE_Automatic     = 1

$strPath = Join-Path $PSScriptRoot $File
if (-not (Test-Path $strPath))
{
  throw "Simulator file not found: $strPath"
}

<#
  Writes the file in a way the agent can live with. The agent opens a simulator file with
  FileAccess.ReadWrite and FileShare.ReadWrite (TFileHandler), so it holds write access for as long
  as it reads. Set-Content asks for FileShare.Read, which denies exactly that access - the write
  then fails with an IOException whenever the agent happens to be reading, and the run dies with it.
  Sharing read and write in both directions lets the two coexist.

  UTF-8 without a BOM, which is how the simulator files are written today.
#>
function Write-SharedText
{
  param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [Parameter(Mandatory = $true)] [string] $Text
  )

  $Stream = [System.IO.File]::Open($Path,
                                   [System.IO.FileMode]::OpenOrCreate,
                                   [System.IO.FileAccess]::Write,
                                   [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
  try
  {
    #The file is rewritten in full, so anything left over from a longer previous content has to go
    $Stream.SetLength(0)
    $Bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes($Text)
    $Stream.Write($Bytes, 0, $Bytes.Length)
    $Stream.Flush()
  }
  finally
  {
    $Stream.Dispose()
  }
}

<#
  Reads the file, applies the given fields and writes it back. A full rewrite is what the agent's
  file watcher reacts to, and Depth 10 keeps nested objects such as Spindle1 intact - the default
  depth of 2 would flatten them into type names.
#>
function Set-SimulatorState
{
  param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [Parameter(Mandatory = $true)] [hashtable] $Fields
  )

  $Json = Get-Content -Path $Path -Raw | ConvertFrom-Json
  foreach ($Key in $Fields.Keys)
  {
    $Json.$Key = $Fields[$Key]
  }
  Write-SharedText -Path $Path -Text ($Json | ConvertTo-Json -Depth 10)
}

$Json = Get-Content -Path $strPath -Raw | ConvertFrom-Json
$iStartCounter = [int] $Json.PartCounter

Write-Host "Simulator : $strPath"
Write-Host "Parts     : $Parts at $CycleTimeSeconds s, stops $MinStopSeconds-$MaxStopSeconds s"
Write-Host "Counter   : starting at $iStartCounter"
Write-Host ""

$Watch = [System.Diagnostics.Stopwatch]::StartNew()
$fProductionSeconds = 0.0
$fStoppedSeconds = 0.0

try
{
  for ($iPart = 1; $iPart -le $Parts; $iPart++)
  {
    #Production: the machine runs for one cycle, then reports the part
    Set-SimulatorState -Path $strPath -Fields @{
      AlarmState    = 0
      OperationMode = $MODE_Automatic
      RunningState  = $RUNNING_Production
    }

    $CycleWatch = [System.Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds ([int]($CycleTimeSeconds * 1000))
    $CycleWatch.Stop()
    $fProductionSeconds += $CycleWatch.Elapsed.TotalSeconds

    $iCounter = $iStartCounter + $iPart
    Set-SimulatorState -Path $strPath -Fields @{ PartCounter = $iCounter }

    #Stop between two parts. The last part is not followed by one - the run simply ends stopped.
    $fStop = 0.0
    if ($iPart -lt $Parts)
    {
      $fStop = [Math]::Round((Get-Random -Minimum $MinStopSeconds -Maximum $MaxStopSeconds), 1)
      Set-SimulatorState -Path $strPath -Fields @{ RunningState = $RUNNING_Stopped }
      Start-Sleep -Milliseconds ([int]($fStop * 1000))
      $fStoppedSeconds += $fStop
    }

    Write-Host ("part {0,4}/{1}  counter={2,-6} cycle={3,5:0.0}s  stop={4,4:0.0}s" -f `
                $iPart, $Parts, $iCounter, $CycleWatch.Elapsed.TotalSeconds, $fStop)
  }
}
finally
{
  #Never leave the machine claiming production - an aborted run would otherwise report an open
  #production segment until somebody notices.
  Set-SimulatorState -Path $strPath -Fields @{ RunningState = $RUNNING_Stopped }

  $Watch.Stop()
  Write-Host ""
  Write-Host ("Run took {0:0.0} s: {1:0.0} s producing, {2:0.0} s stopped." -f `
              $Watch.Elapsed.TotalSeconds, $fProductionSeconds, $fStoppedSeconds)
  if ($Watch.Elapsed.TotalSeconds -gt 0)
  {
    Write-Host ("Availability over the run: {0:0.0} %" -f `
                (100.0 * $fProductionSeconds / $Watch.Elapsed.TotalSeconds))
  }
  Write-Host "Machine left stopped. Stop or pause the job in the platform to close its time."
}

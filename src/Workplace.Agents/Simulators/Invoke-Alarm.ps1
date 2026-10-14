#Requires -Version 5.1
<#
.SYNOPSIS
  Raises or clears alarms of a workplace simulator by rewriting its JSON file.

.DESCRIPTION
  The workplace agent watches these simulator files and reads them back on every change, so writing the
  file is how a simulated machine reports. The alarms of a simulator are the array "Alarms" of its file;
  the alarm pool of the SimulatorControl_v1 and SimulatorRobot_v1 templates reads it (address $.Alarms).
  An entry is

      { "Number": "1024", "Message": "Door open", "Type": "Warning", "Active": true }

  An alarm appears when its entry is in the array and disappears when the entry is removed or "Active" is
  false. "Type" is Information, Warning, Alarm or Emergency; without it the alarm is an Alarm.

  The alarm tracking of ctProduction records every appearance and disappearance per facility component
  (DesignAlarmTracking.md), so this script is how its end-to-end test raises and clears alarms. The agent
  stamps the alarm with the time it reads the file, not with the time in the file.

  Every other field in the file is left exactly as it was.

.PARAMETER File
  Name of the simulator file, resolved next to this script. Example: Simulator1.json

.PARAMETER Raise
  Number of the alarm to raise, for example 1024. An alarm with that number which is already raised is replaced.

.PARAMETER Message
  Text of the alarm to raise. Default: "Alarm <number>".

.PARAMETER Type
  Type of the alarm to raise: Information, Warning, Alarm (default) or Emergency.

.PARAMETER Clear
  Number of the alarm to clear.

.PARAMETER ClearAll
  Clears every alarm.

.PARAMETER List
  Lists the alarms of the file and changes nothing. This is the default without another parameter.

.EXAMPLE
  .\Invoke-Alarm.ps1 -File Simulator1.json -Raise 1024 -Message "Door open" -Type Warning

.EXAMPLE
  .\Invoke-Alarm.ps1 -File Simulator1.json -Clear 1024

.EXAMPLE
  .\Invoke-Alarm.ps1 -File Simulator1.json -ClearAll

.NOTES
  The agent reads the file on its polling cycle, so an alarm shows up on the platform a few seconds after
  the call. The run rewrites the simulator file, so it shows up as a local modification until the
  alarms are cleared again.

  The FileReader driver reports every change of the file as a disappearance and a new appearance of all alarms
  which are still active. With two or more alarms raised at once, the history therefore shows one record per
  file change and alarm, not one record from raise to clear. The platform records what the agent reports.
#>
[CmdletBinding(DefaultParameterSetName = 'List')]
param(
  [Parameter(Mandatory = $true)]
  [string] $File,

  [Parameter(Mandatory = $true, ParameterSetName = 'Raise')]
  [ValidateNotNullOrEmpty()]
  [string] $Raise,

  [Parameter(ParameterSetName = 'Raise')]
  [string] $Message,

  [Parameter(ParameterSetName = 'Raise')]
  [ValidateSet('Information', 'Warning', 'Alarm', 'Emergency')]
  [string] $Type = 'Alarm',

  [Parameter(Mandatory = $true, ParameterSetName = 'Clear')]
  [ValidateNotNullOrEmpty()]
  [string] $Clear,

  [Parameter(Mandatory = $true, ParameterSetName = 'ClearAll')]
  [switch] $ClearAll,

  [Parameter(ParameterSetName = 'List')]
  [switch] $List
)

$ErrorActionPreference = 'Stop'

$strPath = Join-Path $PSScriptRoot $File
if (-not (Test-Path $strPath))
{
  throw "Simulator file not found: $strPath"
}

<#
  Writes the file in a way the agent can live with: it opens a simulator file with FileAccess.ReadWrite and
  FileShare.ReadWrite, so a plain Set-Content, which denies write sharing, fails whenever the agent happens to
  be reading. UTF-8 without a BOM, which is how the simulator files are written today.
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

#The file is read with the same sharing the agent uses
$Reader = New-Object System.IO.StreamReader([System.IO.File]::Open($strPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
try
{
  $State = $Reader.ReadToEnd() | ConvertFrom-Json
}
finally
{
  $Reader.Dispose()
}

#An older file without the array gets it
if ($null -eq $State.PSObject.Properties['Alarms'])
{
  $State | Add-Member -NotePropertyName 'Alarms' -NotePropertyValue @()
}
$Alarms = @($State.Alarms)

switch ($PSCmdlet.ParameterSetName)
{
  'Raise'
  {
    $strText = $Message
    if ([string]::IsNullOrWhiteSpace($strText))
    {
      $strText = "Alarm $Raise"
    }
    $Alarms = @($Alarms | Where-Object { $_.Number -ne $Raise })
    $Alarms += [pscustomobject]@{ Number = $Raise; Message = $strText; Type = $Type; Active = $true }
  }
  'Clear'
  {
    $Alarms = @($Alarms | Where-Object { $_.Number -ne $Clear })
  }
  'ClearAll'
  {
    $Alarms = @()
  }
}

if ($PSCmdlet.ParameterSetName -ne 'List')
{
  $State.Alarms = $Alarms
  #Depth 10 keeps nested objects such as Spindle1 intact, the default depth of 2 would flatten them
  $strJson = $State | ConvertTo-Json -Depth 10
  #PowerShell 5.1 writes an empty array over several lines, the files have it as []
  $strJson = [regex]::Replace($strJson, '\[\s+\]', '[]')
  Write-SharedText -Path $strPath -Text $strJson
}

if ($Alarms.Count -eq 0)
{
  Write-Host "$File : no alarm"
}
else
{
  foreach ($Alarm in $Alarms)
  {
    Write-Host ("{0} : {1,-8} {2,-12} {3}" -f $File, $Alarm.Number, $Alarm.Type, $Alarm.Message)
  }
}

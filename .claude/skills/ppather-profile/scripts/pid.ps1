[CmdletBinding()]
param([string]$Target = 'any', [int]$TargetPid = 0)

. (Join-Path $PSScriptRoot '_common.ps1')

$procId = Get-PatherPid -TargetPid $TargetPid -Target $Target
Write-Host $procId

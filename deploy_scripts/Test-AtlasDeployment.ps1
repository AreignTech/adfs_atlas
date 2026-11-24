#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ConfigPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
$config = Read-AtlasConfig $ConfigPath
Test-AtlasHealth $config

#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$ConfigPath, [string]$ReleaseId)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
$config = Read-AtlasConfig $ConfigPath
if (!$PSCmdlet.ShouldProcess($config.ServerName, 'Restore a previously deployed application release; do not undo database migrations')) { return }
Assert-AtlasServer $config
Assert-AtlasManagedRoot $config
Import-AtlasIis
Assert-AtlasIisOwnership $config
$state = Get-AtlasState $config
if (!$state) { throw 'No successful deployment has been recorded.' }
if (!$ReleaseId) { $ReleaseId = $state.PreviousRelease }
if (!$ReleaseId -or $ReleaseId -eq $state.CurrentRelease) { throw 'Choose a different existing release.' }
$null = Get-AtlasReleasePath $config $ReleaseId
try {
    Set-AtlasRelease $config $ReleaseId
    Test-AtlasHealth $config
    Write-AtlasJson (Join-Path $config.InstallRoot 'deployment-state.json') @{
        SchemaVersion = 1; CurrentRelease = $ReleaseId; PreviousRelease = $state.CurrentRelease; DeployedAtUtc = [DateTime]::UtcNow.ToString('o'); ApiIdentity = $state.ApiIdentity
    }
    Write-Host "Application release restored: $ReleaseId. Database was not rolled back."
} catch {
    $originalError = $_
    try { Set-AtlasRelease $config $state.CurrentRelease; Test-AtlasHealth $config }
    catch { Write-Warning 'Recovery failed. Inspect IIS and service logs.' }
    throw $originalError
}

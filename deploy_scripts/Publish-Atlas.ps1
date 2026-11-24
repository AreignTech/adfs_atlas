#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$SecretsPath,
    [switch]$InitializeServer,
    [switch]$InstallPrerequisites,
    [string]$CertificatePfxPath,
    [Security.SecureString]$CertificatePassword
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
$config = Read-AtlasConfig $ConfigPath
$manifest = Test-AtlasManifest $PackagePath
if ($InstallPrerequisites -and !$InitializeServer) { throw '-InstallPrerequisites requires -InitializeServer.' }
if ($CertificatePfxPath -and !$InitializeServer) { throw 'PFX import requires -InitializeServer.' }
if (!$PSCmdlet.ShouldProcess($config.ServerName, "Publish ATLAS $($manifest.ReleaseId) to $($config.WebsiteUrl); initialize server=$InitializeServer; install prerequisites=$InstallPrerequisites")) { return }
Assert-AtlasServer $config
$null = Read-AtlasSecrets $SecretsPath $config
if ($InitializeServer) {
    $arguments = @{ ConfigPath = $ConfigPath; InstallPrerequisites = $InstallPrerequisites; Confirm = $false }
    if ($CertificatePfxPath) { $arguments.CertificatePfxPath = $CertificatePfxPath }
    if ($CertificatePassword) { $arguments.CertificatePassword = $CertificatePassword }
    & (Join-Path $PSScriptRoot 'Initialize-AtlasServer.ps1') @arguments
}
& (Join-Path $PSScriptRoot 'Deploy-Atlas.ps1') -ConfigPath $ConfigPath -PackagePath $PackagePath -SecretsPath $SecretsPath -Confirm:$false

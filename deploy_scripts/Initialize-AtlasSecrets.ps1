#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Path = "$env:ProgramData\AtlasDeployment\secrets.clixml",
    [switch]$PromptForApiCredential
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
if (!$PSCmdlet.ShouldProcess($Path, 'Capture and DPAPI-encrypt deployment secrets for the current Windows user')) { return }
if ($env:OS -ne 'Windows_NT') { throw 'DPAPI secret capture requires Windows.' }
if (Test-Path -LiteralPath $Path) { throw 'The secrets file already exists. Choose a new filename to avoid accidentally replacing the existing LDAP key.' }
$directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
Assert-AtlasSecretDirectory -Directory $directory -UserSid $identity.User.Value
$null = New-Item -ItemType Directory -Path $directory -Force
# Keep the creating user on the ACL as well as SYSTEM/Administrators.
Set-AtlasAcl -Path $directory -Access @{ ([Security.Principal.WindowsIdentity]::GetCurrent().Name) = 'FullControl' }
Write-AtlasJson (Join-Path $directory '.atlas-secrets-directory.json') @{ Application = 'ATLAS secrets'; UserSid = $identity.User.Value }
$runtime = Read-Host 'Runtime SQL connection string (configured production database; encrypted input)' -AsSecureString
$migration = Read-Host 'Migration SQL connection string (deployment identity with DDL permission; encrypted input)' -AsSecureString
$ldap = Read-Host 'Existing LDAP encryption key (retain exactly when moving existing LDAP configuration)' -AsSecureString
$credential = if ($PromptForApiCredential) { Get-Credential -Message 'Windows identity for the IIS API app pool' } else { $null }
[pscustomobject]@{
    RuntimeConnectionString = $runtime
    MigrationConnectionString = $migration
    LdapEncryptionKey = $ldap
    ApiCredential = $credential
} | Export-Clixml -LiteralPath $Path
Write-Host 'Encrypted secrets saved. They can only be decrypted under this Windows account on this machine.'

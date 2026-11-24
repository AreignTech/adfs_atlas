#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$SecretsPath
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
$config = Read-AtlasConfig $ConfigPath
$manifest = Test-AtlasManifest $PackagePath
$release = Get-AtlasReleasePath $config $manifest.ReleaseId
if (!$PSCmdlet.ShouldProcess($config.ServerName, "Stage $($manifest.ReleaseId), build frontend for $($config.ApiUrl), optionally migrate $($config.DatabaseName), switch IIS/service, verify health, and restore prior code on failure")) { return }
Assert-AtlasServer $config
Assert-AtlasManagedRoot $config
Assert-AtlasTools $config
Import-AtlasIis
Assert-AtlasIisOwnership $config
$secrets = Read-AtlasSecrets $SecretsPath $config
$state = Get-AtlasState $config
if (!(Get-Service -Name $config.FrontendServiceName -ErrorAction SilentlyContinue) -and
    @(Get-NetTCPConnection -State Listen -LocalPort $config.FrontendPort -ErrorAction SilentlyContinue).Count -gt 0) { throw 'The requested Node port is already occupied by another process.' }
if (Test-Path -LiteralPath $release) { throw 'This release folder already exists. Build a new release ID; existing releases are never overwritten.' }
$apiIdentity = if ($config.ApiIdentityMode -eq 'SpecificUser') { $secrets.ApiCredential.UserName } else { "IIS AppPool\$($config.ApiPoolName)" }
if ($state -and $state.ApiIdentity -ine $apiIdentity) { throw 'Changing the API identity requires a separate Data Protection/permissions migration. Existing application identity was preserved.' }
$null = New-Item -ItemType Directory -Path $release
Set-AtlasAcl $release
# Copy only files enumerated and hashed by the build manifest, never arbitrary package extras.
foreach ($entry in $manifest.Files) {
    $source = Assert-AtlasPath -Root $PackagePath -Path (Join-Path $PackagePath $entry.Path)
    $destination = Assert-AtlasPath -Root $release -Path (Join-Path $release $entry.Path)
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
    Copy-Item -LiteralPath $source -Destination $destination
}
$apiPath = Join-Path $release 'api'
$frontend = Join-Path $release 'frontend'
Set-AtlasAcl $apiPath @{ $apiIdentity = 'ReadAndExecute' }
Set-AtlasAcl $frontend @{ 'S-1-5-19' = 'ReadAndExecute' }
Write-AtlasJson -Path (Join-Path $apiPath 'appsettings.Production.json') -Value @{
    ConnectionStrings = @{ Atlas = (ConvertFrom-AtlasSecureString $secrets.RuntimeConnectionString) }
    Atlas = @{ FrontendOrigin = $config.WebsiteUrl; CookieName = 'atlas.session_token'; SessionDays = 7; RequireHttps = $true; AllowSelfRegistration = $false }
    Ldap = @{ EncryptionKey = (ConvertFrom-AtlasSecureString $secrets.LdapEncryptionKey) }
    AllowedHosts = ([uri]$config.ApiUrl).DnsSafeHost
    Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
}
$webConfigPath = Join-Path $apiPath 'web.config'
[xml]$webConfig = Get-Content -LiteralPath $webConfigPath -Raw
$aspNetCore = $webConfig.SelectSingleNode('//aspNetCore')
if (!$aspNetCore) { throw 'Published API web.config has no ASP.NET Core handler.' }
$variables = $aspNetCore.SelectSingleNode('environmentVariables')
if (!$variables) { $variables = $webConfig.CreateElement('environmentVariables'); $null = $aspNetCore.AppendChild($variables) }
$environmentValues = @{
    ASPNETCORE_ENVIRONMENT = 'Production'
    ConnectionStrings__Atlas = (ConvertFrom-AtlasSecureString $secrets.RuntimeConnectionString)
    LDAP_CONFIG_ENCRYPTION_KEY = (ConvertFrom-AtlasSecureString $secrets.LdapEncryptionKey)
}
foreach ($name in $environmentValues.Keys) {
    $environment = $variables.SelectSingleNode("environmentVariable[@name='$name']")
    if (!$environment) { $environment = $webConfig.CreateElement('environmentVariable'); $environment.SetAttribute('name',$name); $null = $variables.AppendChild($environment) }
    $environment.SetAttribute('value',$environmentValues[$name])
}
$webConfig.Save($webConfigPath)

# Build in the final, unique release directory: pnpm's Windows junctions must not be moved later.
# Environment is process-local and restored, including the test distDir switches.
$names = @('ATLAS_API_URL','NODE_ENV','NODE_USE_SYSTEM_CA','ATLAS_PLAYWRIGHT','FLOWBOARD_PLAYWRIGHT')
$previousEnvironment = @{}
foreach ($name in $names) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
try {
    $env:ATLAS_API_URL = $config.ApiUrl.TrimEnd('/')
    $env:NODE_ENV = 'production'
    $env:NODE_USE_SYSTEM_CA = '1'
    [Environment]::SetEnvironmentVariable('ATLAS_PLAYWRIGHT',$null,'Process')
    [Environment]::SetEnvironmentVariable('FLOWBOARD_PLAYWRIGHT',$null,'Process')
    Push-Location $frontend
    try {
        Invoke-AtlasNative 'pnpm.cmd' @('install','--frozen-lockfile','--prod=false','--package-import-method=copy') 'Frontend dependency installation'
        Invoke-AtlasNative 'pnpm.cmd' @('build') 'Frontend production build'
    } finally { Pop-Location }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name],'Process') }
}
Set-AtlasAcl (Join-Path $frontend '.next') @{ 'S-1-5-19' = 'Modify' }
$switchStarted = $false
try {
    # Quiesce writes before migrating. Never seed, drop, restore, or delete production data.
    $switchStarted = $true
    Stop-AtlasWorkloads $config
    if ($config.ApiIdentityMode -eq 'SpecificUser') {
        $password = $secrets.ApiCredential.GetNetworkCredential().Password
        try {
            Set-ItemProperty "IIS:\AppPools\$($config.ApiPoolName)" -Name processModel -Value @{ identityType = 3; userName = $secrets.ApiCredential.UserName; password = $password; loadUserProfile = $true; setProfileEnvironment = $true }
        } finally { $password = $null }
    } else {
        Set-ItemProperty "IIS:\AppPools\$($config.ApiPoolName)" -Name processModel.identityType -Value 4
    }
    if ($config.RunMigrations) {
        $oldConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__Atlas','Process')
        try {
            $env:ConnectionStrings__Atlas = ConvertFrom-AtlasSecureString $secrets.MigrationConnectionString
            Invoke-AtlasNative 'dotnet.exe' @((Join-Path $release 'dbtool\Atlas.DbTool.dll'),'migrate') 'Database migration'
            Invoke-AtlasNative 'dotnet.exe' @((Join-Path $release 'dbtool\Atlas.DbTool.dll'),'verify-schema') 'Database schema verification'
        } finally { [Environment]::SetEnvironmentVariable('ConnectionStrings__Atlas',$oldConnection,'Process') }
    }
    Set-AtlasRelease $config $manifest.ReleaseId
    Test-AtlasHealth $config
    Write-AtlasJson -Path (Join-Path $config.InstallRoot 'deployment-state.json') -Value @{
        SchemaVersion = 1; CurrentRelease = $manifest.ReleaseId
        PreviousRelease = $(if ($state) { $state.CurrentRelease } else { $null })
        DeployedAtUtc = [DateTime]::UtcNow.ToString('o')
        ApiIdentity = $apiIdentity
    }
    Write-Host "Deployed $($manifest.ReleaseId) to $($config.WebsiteUrl). Secrets were not included in the package."
} catch {
    $originalError = $_
    if ($switchStarted -and $state -and $state.CurrentRelease) {
        Write-Warning 'Deployment failed. Restoring the previous application release; database migrations are NOT reversed.'
        try { Set-AtlasRelease $config $state.CurrentRelease; Test-AtlasHealth $config }
        catch { Write-Warning 'Previous-release recovery also failed. Inspect IIS and service logs before reopening traffic.' }
    } elseif ($switchStarted) {
        try {
            Stop-AtlasWorkloads $config
            if (Get-Service -Name $config.FrontendServiceName -ErrorAction SilentlyContinue) { Set-Service -Name $config.FrontendServiceName -StartupType Manual }
            foreach ($name in @($config.ApiSiteName,$config.WebsiteName)) { if ((Get-Website -Name $name).State -ne 'Stopped') { Stop-Website -Name $name } }
        } catch { Write-Warning 'Could not stop every workload after the failed first deployment.' }
    }
    throw $originalError
}

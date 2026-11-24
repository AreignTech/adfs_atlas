#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [switch]$InstallPrerequisites,
    [string]$CertificatePfxPath,
    [Security.SecureString]$CertificatePassword
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
$config = Read-AtlasConfig $ConfigPath
if (!$PSCmdlet.ShouldProcess($config.ServerName, "Provision ATLAS under $($config.InstallRoot): IIS sites/pools, TLS bindings, loopback hosts mapping, optional verified installers, global ARR proxy and firewall rules")) { return }
Assert-AtlasServer $config
$root = $config.InstallRoot
$null = Assert-AtlasPath -Root $root -Path $root -AllowRoot
$marker = Join-Path $root 'deployment-owner.json'
if (Test-Path -LiteralPath $root) {
    if (Test-Path -LiteralPath $marker) { Assert-AtlasManagedRoot $config }
    elseif (@(Get-ChildItem -LiteralPath $root -Force).Count -gt 0) { throw 'InstallRoot is not empty and is not managed by ATLAS deployment. Choose a dedicated folder; no existing files were removed.' }
}
Import-Module ServerManager
$features = @('Web-Server','Web-Mgmt-Console','Web-Scripting-Tools')
if (@(Get-WindowsFeature -Name $features | Where-Object { !$_.Installed }).Count -gt 0) {
    if (!$InstallPrerequisites) { throw 'IIS features are missing. Run with -InstallPrerequisites after supplying verified installer paths.' }
    $result = Install-WindowsFeature -Name $features -IncludeManagementTools
    if (!$result.Success) { throw 'IIS installation failed.' }
    if ($result.RestartNeeded -eq 'Yes') { throw 'Restart Windows, then rerun server initialization.' }
}
if ($InstallPrerequisites) {
    # Validate every supplied installer before executing any of them.
    foreach ($installer in $config.Installers) {
        if ([string]::IsNullOrWhiteSpace($installer.Path)) { continue }
        if ($installer.Kind -notin @('Msi','HostingBundle') -or $installer.Sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
            (Get-FileHash -LiteralPath $installer.Path -Algorithm SHA256).Hash -ne $installer.Sha256) { throw 'An installer kind or SHA-256 hash is invalid.' }
    }
    foreach ($installer in $config.Installers) {
        if ([string]::IsNullOrWhiteSpace($installer.Path)) { continue }
        if ($installer.Path.Contains('"')) { throw 'Invalid installer path.' }
        $file = if ($installer.Kind -eq 'Msi') { "$env:windir\System32\msiexec.exe" } else { $installer.Path }
        $arguments = if ($installer.Kind -eq 'Msi') { "/i `"$($installer.Path)`" /qn /norestart" } else { '/install /quiet /norestart' }
        $process = Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -notin @(0,3010)) { throw "Prerequisite installation failed: $($installer.Name)." }
        if ($process.ExitCode -eq 3010) { throw 'A prerequisite requires a Windows restart. Restart, then rerun initialization.' }
    }
    # Refresh PATH after MSI installation; pin pnpm rather than fetching an unversioned tool.
    $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
    Invoke-AtlasNative 'npm.cmd' @('install','--global',"pnpm@$($config.PnpmVersion)") 'pnpm installation'
}
Assert-AtlasTools $config
Import-AtlasIis
if ($CertificatePfxPath) {
    if (!$CertificatePassword) { $CertificatePassword = Read-Host 'PFX password' -AsSecureString }
    $null = Import-PfxCertificate -FilePath $CertificatePfxPath -CertStoreLocation 'Cert:\LocalMachine\My' -Password $CertificatePassword
}
Assert-AtlasCertificate $config.CertificateThumbprint ([uri]$config.WebsiteUrl).DnsSafeHost
$apiThumbprint = if ($config.ApiCertificateThumbprint) { $config.ApiCertificateThumbprint } else { $config.CertificateThumbprint }
Assert-AtlasCertificate $apiThumbprint ([uri]$config.ApiUrl).DnsSafeHost
if ($config.WinSwSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $config.WinSwSourcePath -Algorithm SHA256).Hash -ne $config.WinSwSha256) { throw 'Supply a trusted WinSW executable and its verified SHA-256 hash.' }

# Refuse to take over another IIS application or Windows service.
foreach ($name in @($config.WebsiteName,$config.ApiSiteName)) {
    $site = Get-Website -Name $name -ErrorAction SilentlyContinue
    if ($site) {
        if (!(Test-Path -LiteralPath $marker)) { throw 'An IIS site name is already in use.' }
        $null = Assert-AtlasPath -Root $root -Path ([Environment]::ExpandEnvironmentVariables($site.PhysicalPath))
    }
}
$webOrigin = [uri]$config.WebsiteUrl; $apiOrigin = [uri]$config.ApiUrl
foreach ($otherSite in @(Get-Website | Where-Object Name -NotIn @($config.WebsiteName,$config.ApiSiteName))) {
    foreach ($otherBinding in $otherSite.Bindings.Collection) {
        if ($otherBinding.bindingInformation -match ':(\d+):([^:]*)$') {
            $port = [int]$Matches[1]; $hostName = $Matches[2]
            if (($hostName -ieq $webOrigin.DnsSafeHost -and $port -in @($webOrigin.Port,80)) -or
                ($hostName -ieq $apiOrigin.DnsSafeHost -and $port -eq $apiOrigin.Port)) { throw 'A requested hostname/port binding belongs to another IIS site.' }
        }
    }
}
foreach ($pool in @($config.WebPoolName,$config.ApiPoolName)) {
    if ((Test-Path "IIS:\AppPools\$pool") -and !(Test-Path -LiteralPath $marker)) { throw 'An app-pool name is already in use.' }
    if (@(Get-Website | Where-Object { $_.ApplicationPool -eq $pool -and $_.Name -notin @($config.WebsiteName,$config.ApiSiteName) }).Count -gt 0) { throw 'An app pool is shared with another site.' }
}
$existingService = Get-CimInstance Win32_Service -Filter "Name='$($config.FrontendServiceName)'"
if ($existingService -and (!(Test-Path -LiteralPath $marker) -or $existingService.PathName -notlike ('*' + (Join-Path $root 'service') + '*'))) { throw 'The service name is already in use.' }
if ($config.ConfigureFirewall -and !(Test-Path -LiteralPath $marker) -and
    (Get-NetFirewallRule -Name ('ATLAS-' + $config.WebsiteName) -ErrorAction SilentlyContinue)) { throw 'The requested firewall rule name is already in use.' }
$null = New-Item -ItemType Directory -Path $root -Force
Set-AtlasAcl $root
foreach ($folder in @('releases','service','logs','iis-web','maintenance-api')) { $null = New-Item -ItemType Directory -Path (Join-Path $root $folder) -Force }
$owner = @{ Application = 'ATLAS'; ServerName = $config.ServerName; WebsiteName = $config.WebsiteName; ApiSiteName = $config.ApiSiteName; WebPoolName = $config.WebPoolName; ApiPoolName = $config.ApiPoolName; FrontendServiceName = $config.FrontendServiceName; WebsiteUrl = $config.WebsiteUrl; ApiUrl = $config.ApiUrl }
Write-AtlasJson $marker $owner
Set-AtlasAcl (Join-Path $root 'service') @{ 'S-1-5-19' = 'ReadAndExecute' }
Set-AtlasAcl (Join-Path $root 'logs') @{ 'S-1-5-19' = 'Modify' }
$serviceFile = Join-Path $root ('service\' + $config.FrontendServiceName + '.exe')
if (!(Test-Path -LiteralPath $serviceFile)) { Copy-Item -LiteralPath $config.WinSwSourcePath -Destination $serviceFile }
elseif ((Get-FileHash -LiteralPath $serviceFile -Algorithm SHA256).Hash -ne $config.WinSwSha256) { throw 'Installed WinSW differs. Upgrade its executable separately with the frontend service stopped.' }
foreach ($pool in @($config.WebPoolName,$config.ApiPoolName)) {
    if (!(Test-Path "IIS:\AppPools\$pool")) { $null = New-WebAppPool -Name $pool }
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$pool" -Name enable32BitAppOnWin64 -Value $false
    Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.setProfileEnvironment -Value $true
    Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty "IIS:\AppPools\$pool" -Name startMode -Value 'AlwaysRunning'
}
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name preserveHostHeader -Value $true
$variables = @(Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables/add' | ForEach-Object { $_.name })
if ('HTTP_X_FORWARDED_PROTO' -notin $variables) { Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{name='HTTP_X_FORWARDED_PROTO'} }
$web = [uri]$config.WebsiteUrl; $api = [uri]$config.ApiUrl
if (!(Get-Website -Name $config.WebsiteName -ErrorAction SilentlyContinue)) {
    $null = New-Website -Name $config.WebsiteName -PhysicalPath (Join-Path $root 'iis-web') -Port 80 -HostHeader $web.DnsSafeHost -ApplicationPool $config.WebPoolName
}
if (!(Get-Website -Name $config.ApiSiteName -ErrorAction SilentlyContinue)) {
    $null = New-Website -Name $config.ApiSiteName -PhysicalPath (Join-Path $root 'maintenance-api') -Port $api.Port -IPAddress '127.0.0.1' -HostHeader $api.DnsSafeHost -ApplicationPool $config.ApiPoolName
    Remove-WebBinding -Name $config.ApiSiteName -Protocol http -Port $api.Port -IPAddress '127.0.0.1' -HostHeader $api.DnsSafeHost
}
foreach ($spec in @(@($config.WebsiteName,$web,'*',$config.CertificateThumbprint),@($config.ApiSiteName,$api,'127.0.0.1',$apiThumbprint))) {
    $information = "$($spec[2]):$($spec[1].Port):$($spec[1].DnsSafeHost)"
    $binding = Get-WebBinding -Name $spec[0] -Protocol https | Where-Object bindingInformation -eq $information
    if (!$binding) {
        New-WebBinding -Name $spec[0] -Protocol https -IPAddress $spec[2] -Port $spec[1].Port -HostHeader $spec[1].DnsSafeHost -SslFlags 1
        $binding = Get-WebBinding -Name $spec[0] -Protocol https | Where-Object bindingInformation -eq $information
    }
    $binding.AddSslCertificate($spec[3],'My')
}
Set-AtlasAcl (Join-Path $root 'iis-web') @{ ("IIS AppPool\" + $config.WebPoolName) = 'ReadAndExecute' }
[IO.File]::WriteAllText((Join-Path $root 'iis-web\web.config'), (New-AtlasProxyConfig $config), (New-Object Text.UTF8Encoding($false)))
foreach ($name in @($config.WebsiteName,$config.ApiSiteName)) {
    Set-WebConfigurationProperty -PSPath "IIS:\Sites\$name" -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $true
    Set-WebConfigurationProperty -PSPath "IIS:\Sites\$name" -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name userName -Value ''
}
if ($config.ManageHostsFile) {
    $hosts = Join-Path $env:windir 'System32\drivers\etc\hosts'
    $hostname = $api.DnsSafeHost
    $lines = @(Get-Content -LiteralPath $hosts)
    $hostMapped = $false
    foreach ($line in $lines) {
        $parts = ($line -split '#',2)[0].Trim() -split '\s+'
        if ($parts.Count -gt 1 -and $hostname -in $parts[1..($parts.Count-1)]) {
            if ($parts[0] -ne '127.0.0.1') { throw 'A conflicting API hosts entry exists. Resolve it explicitly before deployment.' }
            $hostMapped = $true
        }
    }
    if (!$hostMapped) { Add-Content -LiteralPath $hosts -Value "`r`n127.0.0.1 $hostname # ATLAS private API" }
}
if ($config.ConfigureFirewall) {
    $ruleName = 'ATLAS-' + $config.WebsiteName
    if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) { Set-NetFirewallRule -Name $ruleName -Enabled True -Profile $config.FirewallProfiles }
    else { $null = New-NetFirewallRule -Name $ruleName -DisplayName 'ATLAS public HTTPS and HTTP redirect' -Direction Inbound -Action Allow -Protocol TCP -LocalPort @($web.Port,80) -Profile $config.FirewallProfiles }
}
Write-Host 'Server initialized. API certificates/CA trust and public DNS must be valid before health checks. Run Deploy-Atlas.ps1 next.'

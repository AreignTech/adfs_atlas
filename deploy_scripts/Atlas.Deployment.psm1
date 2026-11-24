Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-AtlasConfig {
    param([Parameter(Mandatory)][string]$Path)
    $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($config.SchemaVersion -ne 1) { throw 'Unsupported deployment configuration version.' }
    foreach ($name in @('WebsiteName','WebPoolName','ApiSiteName','ApiPoolName','FrontendServiceName')) {
        if ($config.$name -notmatch '^[A-Za-z][A-Za-z0-9_-]{2,49}$') { throw "Invalid $name." }
    }
    if ($config.FrontendServiceName -notmatch '^[A-Za-z][A-Za-z0-9]{2,49}$') { throw 'The WinSW service name must be alphanumeric.' }
    if (@($config.WebsiteName, $config.ApiSiteName | Select-Object -Unique).Count -ne 2 -or $config.WebPoolName -eq $config.ApiPoolName) { throw 'Use separate IIS sites and application pools.' }
    foreach ($name in @('WebsiteUrl','ApiUrl')) {
        $uri = $null
        if (![uri]::TryCreate($config.$name, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -ne 'https' -or $uri.HostNameType -ne [UriHostNameType]::Dns -or
            $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment -or $uri.UserInfo) {
            throw "$name must be an HTTPS DNS origin without a path, credentials, query, or fragment."
        }
        if ($config.$name -match '[\r\n]') { throw 'URLs cannot contain line breaks.' }
        $config.$name = $uri.GetLeftPart([UriPartial]::Authority)
    }
    $web = [uri]$config.WebsiteUrl; $api = [uri]$config.ApiUrl
    if ($web.Port -eq $api.Port -or $config.FrontendPort -in @($web.Port,$api.Port,80) -or $config.FrontendPort -lt 1024 -or $config.FrontendPort -gt 65535) { throw 'Website, private API, and Node ports must be distinct; Node must use an unprivileged port.' }
    if ($api.Port -eq 80) { throw 'Invalid API port.' }
    if ($config.InstallRoot -notmatch '^[A-Za-z]:\\[^\r\n]+') { throw 'InstallRoot must be an absolute local Windows folder, not a drive root or UNC share.' }
    $root = [IO.Path]::GetFullPath($config.InstallRoot).TrimEnd('\')
    if ($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\')) { throw 'InstallRoot cannot be a drive root.' }
    if ($config.InstallRoot -match '(^|[\\/])\.\.([\\/]|$)' -or $config.InstallRoot.Substring(2).Contains(':') -or $config.InstallRoot.Contains('%')) { throw 'InstallRoot cannot contain parent traversal, environment expansion or alternate data streams.' }
    $config.InstallRoot = $root
    if ($config.NodeExecutable -notmatch '^[A-Za-z]:\\[^"%\r\n]+\.exe$') { throw 'NodeExecutable must be an absolute Windows executable path.' }
    if ($config.PnpmVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Pin a full pnpm version.' }
    if ($config.ApiIdentityMode -notin @('ApplicationPoolIdentity','SpecificUser')) { throw 'Invalid ApiIdentityMode.' }
    if ([string]::IsNullOrWhiteSpace($config.DatabaseName)) { throw 'DatabaseName is required.' }
    if ($config.ServerName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]+$') { throw 'Invalid ServerName.' }
    if ($config.HealthTimeoutSeconds -lt 10 -or $config.HealthTimeoutSeconds -gt 600) { throw 'Health timeout must be 10-600 seconds.' }
    foreach ($profile in $config.FirewallProfiles) { if ($profile -notin @('Domain','Private','Public')) { throw 'Invalid firewall profile.' } }
    foreach ($key in @('RunMigrations','ManageHostsFile','ConfigureFirewall')) { if ($config.$key -isnot [bool]) { throw "$key must be a JSON boolean." } }
    return $config
}

function Assert-AtlasPath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path, [switch]$AllowRoot)
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (!($AllowRoot -and $target.Equals($base,[StringComparison]::OrdinalIgnoreCase)) -and !$target.StartsWith($base + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Path escapes the intended deployment folder.' }
    $cursor = $target
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Deployment paths cannot pass through junctions or symbolic links.' }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $target
}

function Get-AtlasReleasePath {
    param($Config, [string]$ReleaseId)
    if ($ReleaseId -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$') { throw 'Invalid release ID.' }
    Assert-AtlasPath -Root $Config.InstallRoot -Path (Join-Path (Join-Path $Config.InstallRoot 'releases') $ReleaseId)
}

function Assert-AtlasServer {
    param($Config)
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or ![Environment]::Is64BitProcess) { throw 'Run server scripts from elevated 64-bit Windows PowerShell 5.1 (powershell.exe), not pwsh.' }
    if ($env:COMPUTERNAME -ine $Config.ServerName.Split('.')[0]) { throw 'This is not the configured IIS server. Server scripts must run locally on the target.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'An elevated administrator session is required.' }
}

function Invoke-AtlasNative {
    param([string]$File, [string[]]$Arguments, [string]$Label = 'Command')
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit code $LASTEXITCODE)." }
}

function Write-AtlasJson {
    param([string]$Path, $Value)
    $text = $Value | ConvertTo-Json -Depth 20
    $target = [IO.Path]::GetFullPath($Path)
    $temporary = $target + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, $text, (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $target) { [IO.File]::Replace($temporary,$target,[NullString]::Value) }
        else { [IO.File]::Move($temporary,$target) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function Set-AtlasAcl {
    param([string]$Path, [hashtable]$Access = @{})
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $rules = @{ 'S-1-5-18' = 'FullControl'; 'S-1-5-32-544' = 'FullControl' }
    foreach ($key in $Access.Keys) { $rules[$key] = $Access[$key] }
    foreach ($key in $rules.Keys) {
        $principal = if ($key -like 'S-1-*') { New-Object Security.Principal.SecurityIdentifier($key) } else { New-Object Security.Principal.NTAccount($key) }
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($principal,$rules[$key],'ContainerInherit,ObjectInherit','None','Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function ConvertFrom-AtlasSecureString {
    param([Security.SecureString]$Value)
    if ($null -eq $Value) { throw 'A required secret is missing.' }
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Assert-AtlasSecretDirectory {
    param([string]$Directory, [string]$UserSid)
    $null = Assert-AtlasPath -Root $Directory -Path $Directory -AllowRoot
    if (!(Test-Path -LiteralPath $Directory)) { return }
    if (@(Get-ChildItem -LiteralPath $Directory -Force).Count -eq 0) { return }
    $marker = Join-Path $Directory '.atlas-secrets-directory.json'
    if (!(Test-Path -LiteralPath $marker)) { throw 'Choose a dedicated empty secrets directory; existing directory permissions were not changed.' }
    $owner = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($owner.Application -ne 'ATLAS secrets' -or $owner.UserSid -ne $UserSid) { throw 'This secrets directory belongs to another account.' }
}

function Assert-AtlasConnectionString {
    param([string]$ConnectionString, [string]$DatabaseName)
    try {
        $builder = New-Object Data.Common.DbConnectionStringBuilder
        # DbConnectionStringBuilder also implements IDictionary: use the setter
        # explicitly so PowerShell doesn't add a literal "ConnectionString" key.
        $builder.set_ConnectionString($ConnectionString)
        $values = @{}
        foreach ($key in $builder.Keys) {
            $normalized = ([string]$key -replace '[\s_]','').ToLowerInvariant()
            if ($values.ContainsKey($normalized)) { throw 'Ambiguous connection-string aliases.' }
            $values[$normalized] = [string]$builder[$key]
        }
        if ($values.ContainsKey('database') -and $values.ContainsKey('initialcatalog')) { throw 'Ambiguous database aliases.' }
        $database = if ($values.ContainsKey('database')) { $values['database'] } elseif ($values.ContainsKey('initialcatalog')) { $values['initialcatalog'] } else { '' }
        if ($database -ine $DatabaseName) { throw 'Wrong database.' }
        if (!$values.ContainsKey('encrypt') -or $values['encrypt'] -notin @('True','Mandatory','Strict')) { throw 'TLS must be enabled.' }
        if ($values.ContainsKey('trustservercertificate') -and $values['trustservercertificate'] -notin @('False','No')) { throw 'Certificate validation must be enabled.' }
    } catch { throw 'Invalid production connection string: require the configured database, Encrypt=True, and a trusted SQL certificate. Connection details are omitted.' }
}

function Read-AtlasSecrets {
    param([string]$Path, $Config)
    try { $secrets = Import-Clixml -LiteralPath $Path } catch { throw 'Cannot decrypt secrets. Create the secrets file on this server under the same deployment account.' }
    foreach ($key in @('RuntimeConnectionString','MigrationConnectionString','LdapEncryptionKey')) {
        if ($secrets.$key -isnot [Security.SecureString]) { throw 'Invalid encrypted secrets file.' }
    }
    foreach ($key in @('RuntimeConnectionString','MigrationConnectionString')) {
        Assert-AtlasConnectionString -ConnectionString (ConvertFrom-AtlasSecureString $secrets.$key) -DatabaseName $Config.DatabaseName
    }
    if ((ConvertFrom-AtlasSecureString $secrets.LdapEncryptionKey).Length -lt 1) { throw 'Supply the stable LDAP encryption key. Do not silently rotate it during deployment.' }
    if ($Config.ApiIdentityMode -eq 'SpecificUser' -and $secrets.ApiCredential -isnot [pscredential]) { throw 'SpecificUser requires ApiCredential in the encrypted secrets file.' }
    return $secrets
}

function Get-AtlasXmlEscape { param([string]$Value) [Security.SecurityElement]::Escape($Value) }

function New-AtlasProxyConfig {
    param($Config)
    $origin = Get-AtlasXmlEscape $Config.WebsiteUrl.TrimEnd('/')
    $port = [int]$Config.FrontendPort
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><system.webServer>
  <security><requestFiltering><requestLimits maxAllowedContentLength="10485760" /></requestFiltering></security>
  <rewrite><rules>
    <rule name="HTTPS" stopProcessing="true"><match url="(.*)" /><conditions><add input="{HTTPS}" pattern="^OFF$" /></conditions><action type="Redirect" url="$origin/{R:1}" appendQueryString="true" redirectType="Permanent" /></rule>
    <rule name="Next.js" stopProcessing="true"><match url="(.*)" /><serverVariables><set name="HTTP_X_FORWARDED_PROTO" value="https" /></serverVariables><action type="Rewrite" url="http://127.0.0.1:$port/{R:1}" appendQueryString="true" /></rule>
  </rules></rewrite>
</system.webServer></configuration>
"@
}

function New-AtlasServiceConfig {
    param($Config, [string]$ReleasePath)
    $frontend = Get-AtlasXmlEscape (Join-Path $ReleasePath 'frontend')
    $node = Get-AtlasXmlEscape $Config.NodeExecutable
    $api = Get-AtlasXmlEscape $Config.ApiUrl.TrimEnd('/')
    $logs = Get-AtlasXmlEscape (Join-Path $Config.InstallRoot 'logs')
    $name = $Config.FrontendServiceName
    @"
<service>
  <id>$name</id><name>ATLAS Frontend ($name)</name><description>Managed ATLAS Next.js frontend</description>
  <executable>$node</executable>
  <arguments>--use-system-ca &quot;$frontend\node_modules\next\dist\bin\next&quot; start -H 127.0.0.1 -p $($Config.FrontendPort)</arguments>
  <workingdirectory>$frontend</workingdirectory><startmode>Automatic</startmode>
  <env name="NODE_ENV" value="production" /><env name="ATLAS_API_URL" value="$api" />
  <env name="ATLAS_PLAYWRIGHT" value="" /><env name="FLOWBOARD_PLAYWRIGHT" value="" />
  <serviceaccount><domain>NT AUTHORITY</domain><user>LocalService</user></serviceaccount>
  <onfailure action="restart" delay="10 sec" /><stoptimeout>30 sec</stoptimeout>
  <logpath>$logs</logpath><log mode="roll-by-size"><sizeThreshold>10240</sizeThreshold><keepFiles>8</keepFiles></log>
</service>
"@
}

function Test-AtlasManifest {
    param([string]$PackagePath)
    $root = [IO.Path]::GetFullPath($PackagePath)
    $manifest = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 1 -or $manifest.ReleaseId -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$') { throw 'Invalid release manifest.' }
    $seen = @{}
    foreach ($entry in $manifest.Files) {
        if ([IO.Path]::IsPathRooted($entry.Path) -or $entry.Path -match '(^|[\\/])\.\.([\\/]|$)' -or $entry.Path.Contains(':') -or $seen.ContainsKey($entry.Path)) { throw 'Unsafe or duplicate manifest path.' }
        $seen[$entry.Path] = $true
        $path = Assert-AtlasPath -Root $root -Path (Join-Path $root $entry.Path)
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256) { throw 'Package integrity check failed.' }
    }
    foreach ($required in @('api\Atlas.Api.dll','api\web.config','dbtool\Atlas.DbTool.dll','frontend\package.json','frontend\pnpm-lock.yaml','frontend\next.config.ts')) {
        if (!$seen.ContainsKey($required)) { throw "Package is missing $required." }
    }
    return $manifest
}

function Assert-AtlasManagedRoot {
    param($Config)
    $marker = Join-Path $Config.InstallRoot 'deployment-owner.json'
    if (!(Test-Path -LiteralPath $marker)) { throw 'Run Initialize-AtlasServer.ps1 first. This folder is not managed by these scripts.' }
    $owner = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($owner.Application -ne 'ATLAS' -or $owner.ServerName -ine $Config.ServerName -or
        $owner.WebsiteName -ne $Config.WebsiteName -or $owner.ApiSiteName -ne $Config.ApiSiteName -or
        $owner.WebPoolName -ne $Config.WebPoolName -or $owner.ApiPoolName -ne $Config.ApiPoolName -or
        $owner.FrontendServiceName -ne $Config.FrontendServiceName -or $owner.WebsiteUrl -ne $Config.WebsiteUrl -or $owner.ApiUrl -ne $Config.ApiUrl) { throw 'Deployment ownership does not match this configuration. Names/origins of an existing installation cannot be changed implicitly.' }
    $null = Assert-AtlasPath -Root $Config.InstallRoot -Path $Config.InstallRoot -AllowRoot
}

function Assert-AtlasCertificate {
    param([string]$Thumbprint, [string]$Hostname)
    if ($Thumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Supply an existing LocalMachine\My certificate thumbprint.' }
    $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$Thumbprint"
    if (!$certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'TLS certificate is expired, not yet valid, or missing its private key.' }
    $matches = @($certificate.DnsNameList | Where-Object {
        $name = $_.Unicode
        $Hostname -ieq $name -or ($name.StartsWith('*.') -and $Hostname.Split('.').Count -eq $name.Split('.').Count -and $Hostname.EndsWith($name.Substring(1),[StringComparison]::OrdinalIgnoreCase))
    })
    if ($matches.Count -eq 0) { throw 'TLS certificate does not cover the configured hostname.' }
}

function Assert-AtlasTools {
    param($Config)
    if (!(Test-Path -LiteralPath $Config.NodeExecutable)) { throw 'Install the configured Node.js executable.' }
    $version = (& $Config.NodeExecutable --version).TrimStart('v')
    if ([version]$version -lt [version]'24.6.0' -or ([version]$version).Major -notin @(24,26)) { throw 'Use a supported Node.js 24.6+ or 26 LTS with Windows system-CA support.' }
    if (!(Get-Command pnpm.cmd -ErrorAction SilentlyContinue)) { throw 'Install pnpm before deployment.' }
    if ((& pnpm.cmd --version).Trim() -ne $Config.PnpmVersion) { throw 'The installed pnpm version does not match the pinned deployment version.' }
    $runtimes = & dotnet.exe --list-runtimes
    if ($LASTEXITCODE -ne 0 -or !($runtimes -match 'Microsoft.AspNetCore.App 10\.')) { throw 'Install the .NET 10 Hosting Bundle.' }
}

function Import-AtlasIis {
    Import-Module WebAdministration -ErrorAction Stop
    foreach ($module in @('RewriteModule','ApplicationRequestRouting','AspNetCoreModuleV2')) {
        if (!(Get-WebGlobalModule | Where-Object Name -eq $module)) { throw "Missing IIS module: $module." }
    }
}

function Get-AtlasState {
    param($Config)
    $path = Join-Path $Config.InstallRoot 'deployment-state.json'
    if (Test-Path -LiteralPath $path) { return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    return $null
}

function Assert-AtlasIisOwnership {
    param($Config)
    foreach ($spec in @(@($Config.WebsiteName,$Config.WebPoolName),@($Config.ApiSiteName,$Config.ApiPoolName))) {
        $site = Get-Website -Name $spec[0]
        if (!$site -or $site.ApplicationPool -ne $spec[1]) { throw 'Managed IIS site or application pool ownership changed.' }
        $null = Assert-AtlasPath -Root $Config.InstallRoot -Path ([Environment]::ExpandEnvironmentVariables($site.PhysicalPath))
    }
    $service = Get-CimInstance Win32_Service -Filter "Name='$($Config.FrontendServiceName)'"
    if ($service) {
        $executable = Join-Path $Config.InstallRoot ('service\' + $Config.FrontendServiceName + '.exe')
        if ($service.PathName -ine $executable -and $service.PathName -ine ('"' + $executable + '"')) { throw 'The managed service executable was changed; refusing to take it over.' }
        if ($service.StartName -notin @('NT AUTHORITY\LocalService','NT AUTHORITY\LOCAL SERVICE')) { throw 'The frontend service identity changed; expected LocalService.' }
    }
}

function Test-AtlasHealth {
    param($Config)
    $deadline = (Get-Date).AddSeconds($Config.HealthTimeoutSeconds)
    do {
        try {
            $api = Invoke-RestMethod -Uri ($Config.ApiUrl.TrimEnd('/') + '/health/db') -TimeoutSec 10
            if ($api.status -ne 'ok') { throw 'Database health failed.' }
            $local = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$($Config.FrontendPort)/sign-in" -TimeoutSec 10
            if ($local.StatusCode -ne 200) { throw 'Node health failed.' }
            $csrf = Invoke-RestMethod -Uri ($Config.WebsiteUrl.TrimEnd('/') + '/api/atlas/auth/csrf') -TimeoutSec 10
            if ([string]::IsNullOrWhiteSpace($csrf.token)) { throw 'HTTPS/CSRF proxy health failed.' }
            $public = Invoke-WebRequest -UseBasicParsing -Uri ($Config.WebsiteUrl.TrimEnd('/') + '/sign-in') -TimeoutSec 10
            if ($public.StatusCode -ne 200) { throw 'Public website health failed.' }
            Write-Host 'PASS: API database, Node frontend, public HTTPS, and CSRF proxy.'
            return
        } catch {
            if ((Get-Date) -ge $deadline) { throw 'Health checks failed. Check IIS, service logs, SQL access, DNS and certificate trust. No TLS validation was bypassed.' }
            Start-Sleep -Seconds 3
        }
    } while ($true)
}

function Stop-AtlasWorkloads {
    param($Config)
    $service = Get-Service -Name $Config.FrontendServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service -Name $Config.FrontendServiceName
        $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(60))
    }
    if ((Get-WebAppPoolState -Name $Config.ApiPoolName).Value -ne 'Stopped') {
        Stop-WebAppPool -Name $Config.ApiPoolName
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-WebAppPoolState -Name $Config.ApiPoolName).Value -ne 'Stopped') {
            if ((Get-Date) -ge $deadline) { throw 'API app pool did not stop.' }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Set-AtlasRelease {
    param($Config, [string]$ReleaseId)
    $release = Get-AtlasReleasePath $Config $ReleaseId
    if (!(Test-Path -LiteralPath (Join-Path $release 'api\appsettings.Production.json')) -or !(Test-Path -LiteralPath (Join-Path $release 'frontend\.next\BUILD_ID'))) { throw 'Release is incomplete.' }
    $servicePath = Join-Path $Config.InstallRoot ('service\' + $Config.FrontendServiceName)
    Stop-AtlasWorkloads $Config
    Set-ItemProperty "IIS:\Sites\$($Config.ApiSiteName)" -Name physicalPath -Value (Join-Path $release 'api')
    [IO.File]::WriteAllText($servicePath + '.xml', (New-AtlasServiceConfig $Config $release), (New-Object Text.UTF8Encoding($false)))
    if (!(Get-Service -Name $Config.FrontendServiceName -ErrorAction SilentlyContinue)) {
        Invoke-AtlasNative ($servicePath + '.exe') @('install') 'Frontend service installation'
    }
    Start-WebAppPool -Name $Config.ApiPoolName
    foreach ($name in @($Config.ApiSiteName,$Config.WebsiteName)) {
        if ((Get-Website -Name $name).State -ne 'Started') { Start-Website -Name $name }
    }
    Set-Service -Name $Config.FrontendServiceName -StartupType Automatic
    Start-Service -Name $Config.FrontendServiceName
}

Export-ModuleMember -Function *-Atlas*

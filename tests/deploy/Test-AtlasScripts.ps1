#Requires -Version 5.1
# Pure local checks: no IIS changes, services, installers, DNS, or real secrets.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scripts = Join-Path $repo 'deploy_scripts'
Import-Module (Join-Path $scripts 'Atlas.Deployment.psm1') -Force
$testRoot = Join-Path $repo ('build\deployment-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot -Force
$script:passed = 0
function Check([string]$Name, [scriptblock]$Action) {
    & $Action
    $script:passed++
    Write-Host "PASS: $Name"
}
function Equal($Actual, $Expected) { if ($Actual -cne $Expected) { throw 'Unexpected test result.' } }
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Expected the operation to be rejected.' }
}
function ConfigFile([hashtable]$Changes = @{}) {
    $value = Get-Content -LiteralPath (Join-Path $scripts 'deployment.example.json') -Raw | ConvertFrom-Json
    $value.InstallRoot = Join-Path $testRoot 'server'
    foreach ($key in $Changes.Keys) { $value.$key = $Changes[$key] }
    $path = Join-Path $testRoot ([guid]::NewGuid().ToString('N') + '.json')
    Write-AtlasJson $path $value
    return $path
}

Check 'Every script parses in this PowerShell engine' {
    foreach ($file in Get-ChildItem -LiteralPath $scripts -File | Where-Object Extension -In '.ps1','.psm1') {
        $tokens = $null; $errors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
        if ($errors.Count) { throw "$($file.Name) contains PowerShell syntax errors." }
    }
}
$configPath = ConfigFile
$config = Read-AtlasConfig $configPath
Check 'Configuration is prefilled for the requested server and hostname' {
    Equal $config.ServerName 'adfs-websvr-1'
    Equal $config.WebsiteUrl 'https://atlasdev.adfs.alabama.gov'
    Equal $config.ApiUrl 'https://atlasdev.adfs.alabama.gov:5443'
}
Check 'Production origins reject HTTP, paths, credentials, and query strings' {
    foreach ($url in @('http://atlas.example.org','https://atlas.example.org/path','https://user:pass@atlas.example.org','https://atlas.example.org/?x=1')) {
        Reject { Read-AtlasConfig (ConfigFile @{ ApiUrl = $url }) }
    }
}
Check 'Internal and public ports cannot collide' {
    Reject { Read-AtlasConfig (ConfigFile @{ FrontendPort = 443 }) }
    Reject { Read-AtlasConfig (ConfigFile @{ ApiUrl = 'https://api.example.org' }) }
}
Check 'Site names and WinSW names reject injection and ambiguous names' {
    Reject { Read-AtlasConfig (ConfigFile @{ WebsiteName = "bad'pool" }) }
    Reject { Read-AtlasConfig (ConfigFile @{ FrontendServiceName = 'bad-service' }) }
    Reject { Read-AtlasConfig (ConfigFile @{ ApiSiteName = 'AtlasWeb' }) }
}
Check 'Configuration requires real JSON booleans' {
    Reject { Read-AtlasConfig (ConfigFile @{ RunMigrations = 'false' }) }
}
Check 'Install roots reject relative paths, drive roots, traversal and expansion' {
    foreach ($path in @('relative','C:\','C:\Apps\..\Windows','C:\Apps\%TEMP%')) {
        Reject { Read-AtlasConfig (ConfigFile @{ InstallRoot = $path }) }
    }
}
Check 'Path containment rejects sibling-prefix and parent-directory escapes' {
    Equal (Assert-AtlasPath $testRoot (Join-Path $testRoot 'safe\file.json')) (Join-Path $testRoot 'safe\file.json')
    Reject { Assert-AtlasPath $testRoot ($testRoot + '-other\file.json') }
    Reject { Assert-AtlasPath $testRoot (Join-Path $testRoot '..\escape.json') }
}
Check 'Release IDs cannot escape the release directory' {
    Reject { Get-AtlasReleasePath $config '..\outside' }
    Reject { Get-AtlasReleasePath $config 'C:\outside' }
    Equal (Get-AtlasReleasePath $config 'release_1') (Join-Path $config.InstallRoot 'releases\release_1')
}
Check 'SQL database identity and certificate validation are enforced' {
    Assert-AtlasConnectionString 'Server=sql;Database=ATLAS_V2.1;Integrated Security=True;Encrypt=True;TrustServerCertificate=False' 'ATLAS_V2.1'
    Assert-AtlasConnectionString 'Server=sql;Initial Catalog=ATLAS_V2.1;Integrated Security=True;Encrypt=Mandatory' 'ATLAS_V2.1'
    Reject { Assert-AtlasConnectionString 'Server=sql;Database=wrong;Encrypt=True' 'ATLAS_V2.1' }
    Reject { Assert-AtlasConnectionString 'Server=sql;Database=ATLAS_V2.1;Encrypt=False' 'ATLAS_V2.1' }
    Reject { Assert-AtlasConnectionString 'Server=sql;Database=ATLAS_V2.1;Encrypt=True;TrustServerCertificate=True' 'ATLAS_V2.1' }
}
Check 'SQL validation failures do not echo secrets' {
    try { Assert-AtlasConnectionString 'Database=wrong;Password=DO_NOT_ECHO_TEST_PASSWORD;Encrypt=True' 'ATLAS_V2.1' }
    catch { if ($_.ToString().Contains('DO_NOT_ECHO_TEST_PASSWORD')) { throw 'A connection-string secret was exposed.' } }
}
Check 'Connection-string aliases cannot bypass TLS or database validation' {
    Reject { Assert-AtlasConnectionString 'Server=sql;Database=ATLAS_V2.1;Encrypt=True;Trust Server Certificate=True' 'ATLAS_V2.1' }
    Reject { Assert-AtlasConnectionString 'Server=sql;Database=ATLAS_V2.1;Initial Catalog=wrong;Encrypt=True' 'ATLAS_V2.1' }
    Reject { Assert-AtlasConnectionString 'Server=sql;Database=ATLAS_V2.1;Encrypt=True;TrustServerCertificate=False;Trust Server Certificate=True' 'ATLAS_V2.1' }
}
Check 'IIS proxy configuration uses same-origin forwarding, HTTPS and query strings' {
    [xml]$xml = New-AtlasProxyConfig $config
    Equal ($xml.SelectSingleNode('//rule[@name="Next.js"]/action').GetAttribute('url')) 'http://127.0.0.1:3000/{R:1}'
    Equal ($xml.SelectSingleNode('//set[@name="HTTP_X_FORWARDED_PROTO"]').GetAttribute('value')) 'https'
    Equal ($xml.SelectSingleNode('//rule[@name="HTTPS"]/action').GetAttribute('url')) 'https://atlasdev.adfs.alabama.gov/{R:1}'
}
Check 'Service runs as LocalService with TLS validation and no test distDir' {
    [xml]$xml = New-AtlasServiceConfig $config (Join-Path $config.InstallRoot 'releases\test')
    Equal $xml.service.serviceaccount.user 'LocalService'
    if (!$xml.service.arguments.Contains('--use-system-ca') -or !$xml.service.arguments.Contains('-H 127.0.0.1')) { throw 'Service network security settings are missing.' }
    Equal ($xml.SelectSingleNode('//env[@name="ATLAS_API_URL"]').GetAttribute('value')) $config.ApiUrl
    Equal ($xml.SelectSingleNode('//env[@name="ATLAS_PLAYWRIGHT"]').GetAttribute('value')) ''
}
Check 'JSON writes replace prior state atomically and leave no temporary file' {
    $path = Join-Path $testRoot 'state.json'
    Write-AtlasJson $path @{ CurrentRelease = 'one' }
    Write-AtlasJson $path @{ CurrentRelease = 'two' }
    Equal ((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).CurrentRelease) 'two'
    Equal @(Get-ChildItem -LiteralPath $testRoot -Filter '*.tmp').Count 0
}

$package = Join-Path $testRoot 'package'
$null = New-Item -ItemType Directory -Path $package
$files = @()
foreach ($relative in @('api\Atlas.Api.dll','api\web.config','dbtool\Atlas.DbTool.dll','frontend\package.json','frontend\pnpm-lock.yaml','frontend\next.config.ts')) {
    $path = Join-Path $package $relative
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force
    [IO.File]::WriteAllText($path,'synthetic test fixture; never execute')
    $files += [pscustomobject]@{ Path = $relative; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
$manifestPath = Join-Path $package 'manifest.json'
$manifest = @{ SchemaVersion = 1; ReleaseId = 'test_release'; Files = $files }
Write-AtlasJson $manifestPath $manifest
Check 'Release integrity validates every required file' { Equal (Test-AtlasManifest $package).ReleaseId 'test_release' }
Check 'WhatIf plans require no server access or decrypted secrets and create no installation' {
    & (Join-Path $scripts 'Initialize-AtlasServer.ps1') -ConfigPath $configPath -WhatIf
    & (Join-Path $scripts 'Initialize-AtlasSecrets.ps1') -Path (Join-Path $testRoot 'not-created\secrets.clixml') -WhatIf
    & (Join-Path $scripts 'Build-AtlasRelease.ps1') -OutputPath (Join-Path $testRoot 'not-created-build') -WhatIf
    & (Join-Path $scripts 'Deploy-Atlas.ps1') -ConfigPath $configPath -PackagePath $package -SecretsPath (Join-Path $testRoot 'nonexistent-secret.clixml') -WhatIf
    & (Join-Path $scripts 'Publish-Atlas.ps1') -ConfigPath $configPath -PackagePath $package -SecretsPath (Join-Path $testRoot 'nonexistent-secret.clixml') -InitializeServer -WhatIf
    & (Join-Path $scripts 'Restore-AtlasRelease.ps1') -ConfigPath $configPath -WhatIf
    if ((Test-Path -LiteralPath $config.InstallRoot) -or (Test-Path -LiteralPath (Join-Path $testRoot 'not-created-build')) -or (Test-Path -LiteralPath (Join-Path $testRoot 'not-created'))) { throw 'A WhatIf command mutated deployment state.' }
}
Check 'Tampered packages fail before deployment' {
    [IO.File]::WriteAllText((Join-Path $package 'api\Atlas.Api.dll'),'modified')
    Reject { Test-AtlasManifest $package }
    [IO.File]::WriteAllText((Join-Path $package 'api\Atlas.Api.dll'),'synthetic test fixture; never execute')
}
Check 'Manifest rejects traversal, duplicate files, and missing required files' {
    Write-AtlasJson $manifestPath @{ SchemaVersion = 1; ReleaseId = 'test'; Files = @(@{ Path = '..\outside'; Sha256 = 'fake' }) }
    Reject { Test-AtlasManifest $package }
    Write-AtlasJson $manifestPath @{ SchemaVersion = 1; ReleaseId = 'test'; Files = @($files[0],$files[0]) }
    Reject { Test-AtlasManifest $package }
    Write-AtlasJson $manifestPath @{ SchemaVersion = 1; ReleaseId = 'test'; Files = @($files[0]) }
    Reject { Test-AtlasManifest $package }
    Write-AtlasJson $manifestPath $manifest
}
Check 'DPAPI encrypts a synthetic secret and restores it for this user' {
    $path = Join-Path $testRoot 'synthetic-secret.clixml'
    $secret = ConvertTo-SecureString 'SYNTHETIC_DPAPI_TEST' -AsPlainText -Force
    [pscustomobject]@{ Secret = $secret } | Export-Clixml -LiteralPath $path
    if ((Get-Content -LiteralPath $path -Raw).Contains('SYNTHETIC_DPAPI_TEST')) { throw 'Secret was stored as plaintext.' }
    Equal (ConvertFrom-AtlasSecureString (Import-Clixml -LiteralPath $path).Secret) 'SYNTHETIC_DPAPI_TEST'
}
Check 'Secret capture refuses to change unrelated directory ACLs' {
    $directory = Join-Path $testRoot 'unrelated'
    $null = New-Item -ItemType Directory -Path $directory
    [IO.File]::WriteAllText((Join-Path $directory 'keep.txt'),'unrelated data')
    Reject { Assert-AtlasSecretDirectory $directory 'synthetic-sid' }
    Write-AtlasJson (Join-Path $directory '.atlas-secrets-directory.json') @{ Application = 'ATLAS secrets'; UserSid = 'synthetic-sid' }
    Assert-AtlasSecretDirectory $directory 'synthetic-sid'
    Reject { Assert-AtlasSecretDirectory $directory 'other-sid' }
}
Check 'Private directory ACLs protect inheritance and restrict LocalService to read access' {
    $directory = Join-Path $testRoot 'acl-check'
    $null = New-Item -ItemType Directory -Path $directory
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    Set-AtlasAcl $directory @{ $currentUser = 'FullControl'; 'S-1-5-19' = 'ReadAndExecute' }
    $acl = Get-Acl -LiteralPath $directory
    if (!$acl.AreAccessRulesProtected) { throw 'Private ACL inheritance was not protected.' }
    $rule = @($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference.Value -eq 'S-1-5-19' })
    if ($rule.Count -ne 1 -or ($rule[0].FileSystemRights -band [Security.AccessControl.FileSystemRights]::WriteData)) { throw 'LocalService has unexpected write permission.' }
}
Write-Host "$script:passed deployment-script checks passed. Fixtures retained at $testRoot"

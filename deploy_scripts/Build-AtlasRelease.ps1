#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$SourceRoot,
    [string]$ReleaseId = ((Get-Date -Format 'yyyyMMddTHHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Atlas.Deployment.psm1') -Force
if (!$SourceRoot) { $SourceRoot = Split-Path $PSScriptRoot -Parent }
if ($ReleaseId -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$') { throw 'Invalid release ID.' }
$source = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
$output = [IO.Path]::GetFullPath($OutputPath).TrimEnd('\')
if ($output.Equals($source,[StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($output + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'OutputPath must not be the source directory or its ancestor.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new, empty OutputPath for each release.' }
if (!$PSCmdlet.ShouldProcess($output, "Publish Windows x64 API/DbTool and package frontend sources as $ReleaseId (no secrets)")) { return }
if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitProcess) { throw 'Build this Windows deployment package from 64-bit PowerShell on Windows.' }
$null = New-Item -ItemType Directory -Path $output
$stagingParent = [IO.Path]::GetDirectoryName($output)
$staging = Join-Path $stagingParent ('.atlas-build-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $staging
try {
    # RID-specific restore changes NuGet lock graphs. Build a temporary source
    # copy so publishing never edits the working checkout or its lockfiles.
    $backend = Join-Path $source 'backend'
    foreach ($file in Get-ChildItem -LiteralPath $backend -Recurse -File | Where-Object {
        $_.FullName.Substring($backend.Length) -notmatch '[\\/](bin|obj)[\\/]' -and
        ($_.Extension -in @('.cs','.csproj','.props','.targets','.sql') -or $_.Name -eq 'packages.lock.json')
    }) {
        $null = Assert-AtlasPath -Root $backend -Path $file.FullName
        $destination = Join-Path (Join-Path $staging 'backend') $file.FullName.Substring($backend.Length + 1)
        $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    if (Test-Path -LiteralPath (Join-Path $source 'global.json')) { Copy-Item -LiteralPath (Join-Path $source 'global.json') -Destination $staging }
    foreach ($project in @(@('Atlas.Api','api'),@('Atlas.DbTool','dbtool'))) {
        Invoke-AtlasNative 'dotnet.exe' @('publish',(Join-Path $staging "backend\$($project[0])\$($project[0]).csproj"),'-c','Release','-r','win-x64','--self-contained','false','--disable-build-servers','-p:UseSharedCompilation=false','-o',(Join-Path $output $project[1])) "$($project[0]) publish"
    }
} finally {
    # Only remove our fresh staging folder after checking its absolute target.
    $verifiedStaging = Assert-AtlasPath -Root $stagingParent -Path $staging
    if (@(Get-ChildItem -LiteralPath $verifiedStaging -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -eq 0) {
        Remove-Item -LiteralPath $verifiedStaging -Recurse -Force
    } else { Write-Warning 'A build staging junction was detected; staging was retained for inspection.' }
}
# The CLI must never carry a developer's default SQL connection into deployment.
foreach ($relative in @('dbtool\appsettings.json','api\appsettings.json','api\appsettings.Development.json','api\appsettings.Production.json')) {
    $path = Assert-AtlasPath -Root $output -Path (Join-Path $output $relative)
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
}
$frontend = Join-Path $output 'frontend'
$null = New-Item -ItemType Directory -Path $frontend
foreach ($file in @('package.json','pnpm-lock.yaml','next.config.ts','tsconfig.json','postcss.config.mjs','eslint.config.mjs','components.json')) {
    $path = Join-Path $source $file
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $frontend }
}
foreach ($folder in @('src','public')) {
    $path = Join-Path $source $folder
    if (Get-ChildItem -LiteralPath $path -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Frontend source directories cannot contain reparse points.' }
    Copy-Item -LiteralPath $path -Destination $frontend -Recurse
}
$files = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($output.Length + 1); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
Write-AtlasJson -Path (Join-Path $output 'manifest.json') -Value @{ SchemaVersion = 1; ReleaseId = $ReleaseId; Files = $files }
$null = Test-AtlasManifest $output
Write-Host "Release package ready: $output"

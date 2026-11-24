# Automated Windows IIS deployment

These scripts deploy ATLAS to **one Windows IIS server**. Run server scripts
locally in elevated, **64-bit Windows PowerShell 5.1** (`powershell.exe`), not
PowerShell 7. They do not configure WinRM or connect to the server remotely.

The example is prefilled for `adfs-websvr-1` and
`https://atlasdev.adfs.alabama.gov`. The private API defaults to
`https://atlasdev.adfs.alabama.gov:5443`, bound to `127.0.0.1`. This allows the
same TLS certificate to cover the website and API. Node listens on
`127.0.0.1:3000`. The API URL is configurable and must be HTTPS without a path.

## Scripts

| File | Purpose |
| --- | --- |
| `Build-AtlasRelease.ps1` | Publish Windows x64 API/DbTool and package allowlisted frontend sources with a SHA-256 integrity manifest. |
| `Initialize-AtlasSecrets.ps1` | Prompt for secrets and save Windows DPAPI-encrypted CLIXML. |
| `Initialize-AtlasServer.ps1` | Optional approved installers, IIS roles/sites/pools, certificates, ARR, folder ACLs, hosts mapping and firewall rules. |
| `Publish-Atlas.ps1` | One entry point: optionally initialize, deploy, and verify. |
| `Deploy-Atlas.ps1` | Stage/build a release, write protected runtime settings, migrate, switch IIS/service, verify health, recover prior code on failure. |
| `Test-AtlasDeployment.ps1` | Check API database, Node, public HTTPS, and secure CSRF proxy. |
| `Restore-AtlasRelease.ps1` | Restore retained application code without reversing database migrations. |
| `Atlas.Deployment.psm1` | Shared validation, integrity, configuration, permissions, service, and health helpers. |

## Required inputs

- Public DNS pointing `atlasdev.adfs.alabama.gov` to the IIS server.
- A valid, trusted TLS certificate with its private key, installed in
  `LocalMachine\My`, or supplied as a PFX. Supply the thumbprint. If using a
  different API hostname, its certificate must cover that hostname too.
- An existing database and two connection strings: runtime read/write access
  and migration access with DDL permissions. They can differ for least privilege.
- A trusted SQL TLS certificate. Connection strings must enable encryption,
  name the configured database, and not bypass certificate validation.
- The **existing LDAP encryption key** if keeping encrypted LDAP configuration.
  Do not generate a replacement during a server move. For a fresh deployment,
  generate and retain a stable random key in your approved secret store.
- An approved WinSW x64 executable and independently verified SHA-256 hash.
- Target access to npm or an approved internal registry under the deployment
  account. Frontend dependencies/builds are created in their final release
  directory to avoid moving Windows pnpm junctions.
- Windows/domain policy allowing the chosen IIS app-pool identity to run.

For Windows-integrated SQL on another machine, normally set
`ApiIdentityMode` to `SpecificUser` and capture a dedicated domain credential.
Have the DBA grant it the required database rights. `ApplicationPoolIdentity`
uses `IIS AppPool\AtlasApi` locally and may use the machine account for network
access. SQL authentication can use the default application-pool identity.

DNS/CA issuance and trust policy, SQL backup/restore and login grants, domain
account creation and policy require the corresponding infrastructure permissions.
Those are inputs, not changes inferred from a website URL. The scripts never
drop, restore, or seed the database, change execution policy, or reboot Windows.

## 1. Build on the Windows development/build machine

Install the .NET 10 SDK. From the repository root:

```powershell
powershell.exe -NoProfile -File .\deploy_scripts\Build-AtlasRelease.ps1 `
  -OutputPath D:\AtlasPublish\release-001
```

Use a new output folder each time. Builds use a temporary backend source copy
so RID-specific restore does not modify repository lockfiles. Only that fresh
staging folder is removed, after path checks. The resulting package contains
`api`, `dbtool`, frontend **sources**, and `manifest.json`. It excludes developer
appsettings, `.env` files, credentials, and development `node_modules`.

Transfer packages over an approved authenticated channel. Hashes detect
corruption, not publisher identity. Unlisted package files are not deployed.

## 2. Prepare the server configuration

Copy the full `deploy_scripts` folder and release package to, for example:

```text
C:\AtlasDeployment\deploy_scripts\
C:\AtlasDeployment\packages\release-001\
```

On `adfs-websvr-1`, in elevated Windows PowerShell:

```powershell
Set-Location C:\AtlasDeployment
Copy-Item .\deploy_scripts\deployment.example.json .\deployment.local.json
notepad .\deployment.local.json
```

Fill in certificate thumbprint(s), `WinSwSourcePath`, `WinSwSha256`, and the actual
`DatabaseName`. The example database is `ATLAS_V2.1`; change it if necessary.
Choose `ApiIdentityMode`. Use a dedicated, empty `InstallRoot`. The scripts refuse
to adopt another site's folders, IIS names, service or bindings. To migrate an
existing manual installation, choose distinct names/root for a side-by-side move.

Site/pool/service names and origins are fixed for an initialized installation;
changing them requires a planned migration. Certificate renewal can update the
thumbprint for the same origins through initialization.

Never put SQL passwords, connection strings, LDAP keys or PFX passwords in the
JSON configuration or source control. Local `deployment.*.json`, CLIXML and PFX
files in this scripts folder are ignored by Git, except the example JSON.

## 3. Capture secrets on the target under the deployment account

```powershell
.\deploy_scripts\Initialize-AtlasSecrets.ps1
```

Add `-PromptForApiCredential` when using `SpecificUser`. The prompts capture the
runtime SQL connection, migration SQL connection, stable LDAP key, and optional
API Windows credential. The default file is:

```text
C:\ProgramData\AtlasDeployment\secrets.clixml
```

DPAPI requires the **same Windows account and machine** when decrypting. Creating
the file on a workstation and copying it to IIS will not work. For credential
rotation, create a new encrypted filename; retain the LDAP key exactly.

At deployment time, secrets are decrypted in memory into normal .NET/IIS
runtime configuration. Those release files are protected by NTFS ACLs for
Administrators, SYSTEM and the API identity, but are not encrypted vault files.
Protect server/backup access. The frontend LocalService identity cannot read
the API secrets. Secret values are not command-line arguments or package data.

## 4. Prerequisites and certificates

Preinstall IIS Management/Scripting Tools, .NET 10 Hosting Bundle, supported
Node.js 24.6+ LTS, pnpm 10.33.0, URL Rewrite, ARR, and the TLS certificate, or
provide approved local installer paths and SHA-256 values in `Installers` and
run initialization with `-InstallPrerequisites`. This installs IIS first, then
the supplied MSI/Hosting Bundle packages, and pinned pnpm from the registry.

Obtain hashes independently from vendors or your approved software-distribution
process. Merely calculating a downloaded file's hash does not establish trust.
Installers requiring reboot stop the script: restart in your maintenance window
and rerun. Prerequisite installation is not transactionally rolled back.

Optional PFX import: add `-CertificatePfxPath C:\Installers\atlas.pfx` to the
initial publish command. Its password is prompted securely, or pass a
SecureString using `-CertificatePassword`. The configured thumbprint must match.
CA trust still follows Windows/domain policy; certificate checks are not bypassed.

## 5. Preview and execute

```powershell
# Preview: no secret decryption, server changes or network deployment.
.\deploy_scripts\Publish-Atlas.ps1 `
  -ConfigPath .\deployment.local.json `
  -PackagePath .\packages\release-001 `
  -SecretsPath C:\ProgramData\AtlasDeployment\secrets.clixml `
  -InitializeServer -WhatIf

# First deployment with preinstalled prerequisites/certificate.
.\deploy_scripts\Publish-Atlas.ps1 `
  -ConfigPath .\deployment.local.json `
  -PackagePath .\packages\release-001 `
  -SecretsPath C:\ProgramData\AtlasDeployment\secrets.clixml `
  -InitializeServer
```

Add `-InstallPrerequisites` when installing configured packages. Later releases
omit `-InitializeServer`, installer options and PFX arguments.

Initialization enables ARR and `preserveHostHeader` at server level and allows
`HTTP_X_FORWARDED_PROTO`. Review compatibility on shared IIS servers. Optional
firewall rules allow only the website HTTPS port and HTTP 80 redirect; API/Node
bind to loopback. With `ManageHostsFile`, the API name maps to `127.0.0.1` on this
server only. With the default shared hostname, the website also resolves locally;
external DNS remains unchanged. Conflicting hosts entries cause an error.

Deployment stages a unique release, installs/builds the frontend with the final
API URL, and only then stops the current frontend/API. It runs `migrate` and
`verify-schema` when enabled, switches IIS physical path and Windows service,
starts workloads, checks health, and atomically records current/previous versions.
The frontend runs as LocalService. API Load User Profile/Set Profile Environment
remain enabled for persistent Data Protection keys. API identity changes are
rejected after a successful deployment because those require a separate plan.

Take your normal SQL backup before migrations. A failed switch/migration/health
check attempts to restore prior code; a failed first deployment stops workloads.
**Code rollback never reverses database migrations**, and cannot guarantee old
code works with an incompatible schema. Plan database recovery with your DBA.

## 6. Later releases, checks and rollback

```powershell
.\deploy_scripts\Publish-Atlas.ps1 `
  -ConfigPath .\deployment.local.json `
  -PackagePath .\packages\release-002 `
  -SecretsPath C:\ProgramData\AtlasDeployment\secrets.clixml

.\deploy_scripts\Test-AtlasDeployment.ps1 -ConfigPath .\deployment.local.json
.\deploy_scripts\Restore-AtlasRelease.ps1 -ConfigPath .\deployment.local.json -WhatIf
.\deploy_scripts\Restore-AtlasRelease.ps1 -ConfigPath .\deployment.local.json
```

`-ReleaseId <id>` selects another retained version using its manifest ID (not
the original package folder name). Releases are not deleted automatically;
their retained runtime configuration remains sensitive.

Frontend logs: `<InstallRoot>\logs`. API startup: Windows Event Viewer/IIS logs.
Version pointers: `<InstallRoot>\deployment-state.json`. Health checks verify SQL,
Node, public HTTPS and CSRF, but do not sign in as a production user, exercise
LDAP, or prove external DNS/firewall access. Perform those staging acceptance
checks before go-live. Scripts follow your execution/signing policy unchanged.

## Local tests and references

```powershell
powershell.exe -NoProfile -File .\tests\deploy\Test-AtlasScripts.ps1
```

Tests use synthetic workspace fixtures, not IIS, services, installers, migrations
or the target server. Real provisioning requires a staging run on Windows Server
with the actual certificates, identities, SQL permissions and network policy.

Official references:
[ASP.NET Core IIS hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0),
[ARR/URL Rewrite](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/reverse-proxy-with-url-rewrite-v2-and-application-request-routing),
[.NET downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
[Node.js](https://nodejs.org/en/download),
[Node CA trust](https://nodejs.org/en/learn/http/enterprise-network-configuration),
[WinSW releases](https://github.com/winsw/winsw/releases),
[WinSW XML](https://github.com/winsw/winsw/blob/v2.12.0/doc/xmlConfigFile.md),
[Next.js self-hosting](https://nextjs.org/docs/app/guides/self-hosting).

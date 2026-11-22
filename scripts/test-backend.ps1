$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $containerState = docker inspect atlas-ldap-verification --format '{{.State.Running}}' 2>$null
    if ($LASTEXITCODE -ne 0) {
        docker run -d --name atlas-ldap-verification --hostname localhost -p 127.0.0.1:1389:389 -p 127.0.0.1:1636:636 -e LDAP_ORGANISATION=ATLAS-Verification -e LDAP_DOMAIN=atlas.test -e LDAP_ADMIN_PASSWORD=DirectoryTest123! -e LDAP_TLS_VERIFY_CLIENT=never osixia/openldap:1.5.0
        if ($LASTEXITCODE -ne 0) { throw 'LDAP fixture startup failed.' }
    } elseif ($containerState -ne 'true') { docker start atlas-ldap-verification | Out-Null }
    # The legacy image's bundled CA is expired. Generate a current test-only
    # localhost certificate; never install it in the machine trust store.
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    $san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName('localhost')
    $san.AddIpAddress([System.Net.IPAddress]::Loopback)
    $request.CertificateExtensions.Add($san.Build())
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(1))
    $certificate.ExportCertificatePem() | docker exec -i atlas-ldap-verification sh -c 'cat > /container/service/slapd/assets/certs/ldap.crt'
    $certificate.ExportCertificatePem() | docker exec -i atlas-ldap-verification sh -c 'cat > /container/service/slapd/assets/certs/ca.crt'
    $rsa.ExportPkcs8PrivateKeyPem() | docker exec -i atlas-ldap-verification sh -c 'cat > /container/service/slapd/assets/certs/ldap.key'
    $certificate.Dispose()
    $rsa.Dispose()
    docker restart atlas-ldap-verification | Out-Null
    $ready = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        docker exec atlas-ldap-verification ldapwhoami -x -H ldap://localhost -D cn=admin,dc=atlas,dc=test -w 'DirectoryTest123!' 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'LDAP fixture did not become ready.' }
    Get-Content tests/ldap-user.ldif -Raw | docker exec -i atlas-ldap-verification ldapadd -x -D cn=admin,dc=atlas,dc=test -w 'DirectoryTest123!' 2>$null | Out-Null
    if ($LASTEXITCODE -notin @(0, 68)) { throw 'LDAP fixture user creation failed.' }
    $env:ATLAS_LDAP_TEST_CA = (docker exec atlas-ldap-verification cat /container/service/slapd/assets/certs/ca.crt) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not obtain the public LDAP fixture CA.' }
    dotnet test backend/Atlas.Tests -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=minimal'
    $testExit = $LASTEXITCODE
} finally { Pop-Location }
exit $testExit

# Disposable test credentials. Never reads or replaces the game's real certificates.
param([switch]$ReplaceTestCertificate)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$destination = Join-Path $repoRoot 'LocalData/EmbeddedIce/check-certificate'
if ((Test-Path -LiteralPath $destination) -and !$ReplaceTestCertificate) { throw 'Check certificate directory already exists. Use -ReplaceTestCertificate to regenerate only these disposable test credentials.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$caKey = [Security.Cryptography.RSA]::Create(2048)
$serverKey = [Security.Cryptography.RSA]::Create(2048)
try {
    $sha = [Security.Cryptography.HashAlgorithmName]::SHA256
    $padding = [Security.Cryptography.RSASignaturePadding]::Pkcs1
    $caRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Moon ICE isolated check CA', $caKey, $sha, $padding)
    $caRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    $caRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign, $true))
    $now = [DateTimeOffset]::UtcNow
    $ca = $caRequest.CreateSelfSigned($now.AddMinutes(-5), $now.AddDays(7))
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=moon-ice-check.invalid', $serverKey, $sha, $padding)
        $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddDnsName('moon-ice-check.invalid')
        $request.CertificateExtensions.Add($san.Build())
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $oids = [Security.Cryptography.OidCollection]::new()
        $oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1')) | Out-Null
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $false))
        $serial = [byte[]]::new(16)
        [Security.Cryptography.RandomNumberGenerator]::Fill($serial)
        $certificate = $request.Create($ca, $now.AddMinutes(-4), $now.AddDays(2), $serial)
        try {
            [IO.File]::WriteAllText((Join-Path $destination 'ca.pem'), $ca.ExportCertificatePem())
            [IO.File]::WriteAllText((Join-Path $destination 'server.pem'), $certificate.ExportCertificatePem())
            [IO.File]::WriteAllText((Join-Path $destination 'server-key.pem'), $serverKey.ExportPkcs8PrivateKeyPem())
        } finally { $certificate.Dispose() }
    } finally { $ca.Dispose() }
} finally { $caKey.Dispose(); $serverKey.Dispose() }
Write-Output 'Created isolated test certificate under LocalData/EmbeddedIce/check-certificate (not a game credential).'

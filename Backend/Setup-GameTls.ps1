param([Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9.-]+$')][string]$ServerName)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$tlsDirectory = Join-Path $projectRoot 'LocalData/game-tls'
$publicCertificate = Join-Path $projectRoot 'moon-game-ca.pem'
if ((Test-Path -LiteralPath $tlsDirectory) -or (Test-Path -LiteralPath $publicCertificate)) {
    throw 'Game certificates already exist. Refusing to replace the server identity or client trust. Inspect/rotate them explicitly.'
}
$opensslCommand = Get-Command openssl -ErrorAction SilentlyContinue
$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$opensslPath = if ($opensslCommand) { $opensslCommand.Source } elseif ($gitCommand) {
    Join-Path (Split-Path (Split-Path $gitCommand.Source -Parent) -Parent) 'usr/bin/openssl.exe'
} else { Join-Path $env:ProgramFiles 'Git/usr/bin/openssl.exe' }
if (!(Test-Path -LiteralPath $opensslPath)) { throw 'OpenSSL was not found. Install Git for Windows or add OpenSSL to PATH.' }
New-Item -ItemType Directory -Path $tlsDirectory -Force | Out-Null
$privateAcl = Get-Acl -LiteralPath $tlsDirectory
$privateAcl.SetAccessRuleProtection($true, $false)
foreach ($identity in @([System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18', 'S-1-5-32-544')) {
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        (New-Object System.Security.Principal.SecurityIdentifier($identity)), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $privateAcl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $tlsDirectory -AclObject $privateAcl
function Invoke-OpenSsl {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $opensslPath @Arguments 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'OpenSSL failed. Retain the partial directory for diagnosis; do not distribute it.' }
    } finally { $ErrorActionPreference = $previousPreference }
}
Invoke-OpenSsl @('req','-x509','-newkey','rsa:2048','-nodes','-days','3650','-sha256','-subj','/CN=Moonkov Game CA',
    '-addext','basicConstraints=critical,CA:TRUE','-addext','keyUsage=critical,keyCertSign,cRLSign',
    '-keyout',(Join-Path $tlsDirectory 'ca-key.pem'),'-out',(Join-Path $tlsDirectory 'ca.pem'))
Invoke-OpenSsl @('req','-new','-newkey','rsa:2048','-nodes','-sha256','-subj',("/CN=" + $ServerName),
    '-keyout',(Join-Path $tlsDirectory 'server-key.pem'),'-out',(Join-Path $tlsDirectory 'server.csr'))
$extensionsFile = Join-Path $tlsDirectory 'server.ext'
@("subjectAltName=DNS:$ServerName",'basicConstraints=critical,CA:FALSE','keyUsage=critical,digitalSignature,keyEncipherment','extendedKeyUsage=serverAuth') |
    Set-Content -LiteralPath $extensionsFile -Encoding ascii
Invoke-OpenSsl @('x509','-req','-in',(Join-Path $tlsDirectory 'server.csr'),'-CA',(Join-Path $tlsDirectory 'ca.pem'),
    '-CAkey',(Join-Path $tlsDirectory 'ca-key.pem'),'-CAcreateserial','-days','825','-sha256','-extfile',$extensionsFile,
    '-out',(Join-Path $tlsDirectory 'server.pem'))
Invoke-OpenSsl @('verify','-CAfile',(Join-Path $tlsDirectory 'ca.pem'),'-verify_hostname',$ServerName,(Join-Path $tlsDirectory 'server.pem'))
Copy-Item -LiteralPath (Join-Path $tlsDirectory 'ca.pem') -Destination $publicCertificate
Write-Host 'Created server DTLS identity. Distribute only moon-game-ca.pem with clients. Keep all LocalData/game-tls private.'

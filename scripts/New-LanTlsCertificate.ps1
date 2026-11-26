[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DnsName,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNull()]
    [SecureString]$Password,

    [ValidateRange(30, 1825)]
    [int]$ValidDays = 365
)

$ErrorActionPreference = 'Stop'

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$certificate = New-SelfSignedCertificate `
    -Subject "CN=$DnsName" `
    -DnsName $DnsName `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -NotAfter (Get-Date).AddDays($ValidDays) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.1')

try {
    Export-PfxCertificate `
        -Cert $certificate `
        -FilePath $resolvedOutput `
        -Password $Password `
        -Force | Out-Null

    $sha256 = $certificate.GetCertHashString(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    Write-Host "Đã tạo chứng thư TLS: $resolvedOutput"
    Write-Host "SHA-256 dùng trên Desktop: $sha256"
}
finally {
    Remove-Item -LiteralPath $certificate.PSPath -Force -ErrorAction SilentlyContinue
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PfxPath,

    [Parameter(Mandatory)]
    [string] $CerPath,

    [Parameter(Mandatory)]
    [string] $Password
)

$ErrorActionPreference = 'Stop'
$publisher = 'CN=Achievement Relay Open Source'
$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $publisher `
    -FriendlyName 'Achievement Relay development package signing' `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -KeyExportPolicy Exportable `
    -HashAlgorithm SHA256 `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears(2) `
    -TextExtension @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.3',
        '2.5.29.19={text}'
    )

try {
    $securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
    Export-PfxCertificate -Cert $certificate -FilePath $PfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $certificate -FilePath $CerPath -Type CERT | Out-Null
}
finally {
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force
}

Write-Host "Created temporary development signing files for $publisher."

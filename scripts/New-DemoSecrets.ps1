param(
    [Parameter(Mandatory = $true)]
    [string]$ClientEmail,

    [Parameter(Mandatory = $true)]
    [string]$AdminEmail,

    [Parameter(Mandatory = $true)]
    [string]$SmtpHost,

    [Parameter(Mandatory = $true)]
    [string]$SmtpUsername,

    [Parameter(Mandatory = $true)]
    [string]$SmtpFrom,

    [int]$SmtpPort = 587,
    [ValidateSet("Auto", "StartTls", "SslOnConnect")]
    [string]$SmtpSecurity = "StartTls",
    [string]$ConnectionString = "Server=(localdb)\MSSQLLocalDB;Database=NetCafeManager;Trusted_Connection=True;TrustServerCertificate=True",
    [decimal]$HourlyRate = 10000,
    [string]$OutputPath = ".demo-secrets.ps1",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "File $OutputPath đã tồn tại. Dùng -Force nếu muốn tạo lại toàn bộ secret demo."
}

if ($SmtpPort -lt 1 -or $SmtpPort -gt 65535) {
    throw "SmtpPort phải nằm trong khoảng 1-65535."
}

if ($HourlyRate -le 0) {
    throw "HourlyRate phải là số dương."
}

function New-RandomBase64([int]$ByteCount = 32) {
    $bytes = [byte[]]::new($ByteCount)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    return [Convert]::ToBase64String($bytes)
}

function New-DemoPassword {
    $bytes = [byte[]]::new(16)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    $hex = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    return "Demo!" + $hex
}

function Quote-PowerShell([string]$Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

$secureSmtpPassword = Read-Host "Nhập mật khẩu/app password SMTP" -AsSecureString
$smtpPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSmtpPassword)
try {
    $smtpPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($smtpPasswordPointer)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($smtpPasswordPointer)
}

$machineKey = New-RandomBase64
$otpSecret = New-RandomBase64
$clientPassword = New-DemoPassword
$adminPassword = New-DemoPassword
$machineKeysJson = [ordered]@{ "MAY-01" = $machineKey } | ConvertTo-Json -Compress

$values = [ordered]@{
    NETCAFE_DB_CONNECTION = $ConnectionString
    NETCAFE_OTP_HASH_SECRET_BASE64 = $otpSecret
    NETCAFE_MACHINE_KEYS_JSON = $machineKeysJson
    NETCAFE_HOURLY_RATE = $HourlyRate.ToString([Globalization.CultureInfo]::InvariantCulture)
    NETCAFE_TCP_PORT = "5050"
    NETCAFE_SMTP_HOST = $SmtpHost
    NETCAFE_SMTP_PORT = $SmtpPort.ToString()
    NETCAFE_SMTP_SECURITY = $SmtpSecurity
    NETCAFE_SMTP_USERNAME = $SmtpUsername
    NETCAFE_SMTP_PASSWORD = $smtpPassword
    NETCAFE_SMTP_FROM = $SmtpFrom
    NETCAFE_SMTP_FROM_NAME = "NetCafeManager"
    NETCAFE_DEMO_CLIENT_USERNAME = "demo-client"
    NETCAFE_DEMO_CLIENT_EMAIL = $ClientEmail
    NETCAFE_DEMO_CLIENT_PASSWORD = $clientPassword
    NETCAFE_DEMO_ADMIN_USERNAME = "demo-admin"
    NETCAFE_DEMO_ADMIN_EMAIL = $AdminEmail
    NETCAFE_DEMO_ADMIN_PASSWORD = $adminPassword
    NETCAFE_DEMO_REPLACE_EXISTING = "false"
}

$lines = @(
    "# File được tạo tự động. Không commit, chia sẻ hoặc chụp màn hình nội dung."
)
foreach ($entry in $values.GetEnumerator()) {
    $lines += ('$env:{0} = {1}' -f $entry.Key, (Quote-PowerShell $entry.Value))
}

Set-Content -LiteralPath $OutputPath -Value $lines -Encoding utf8
Write-Host "Đã tạo $OutputPath. File này đã được .gitignore loại khỏi Git."
Write-Host "Nạp biến môi trường bằng lệnh: . .\$OutputPath"
Write-Warning "Không commit, gửi hoặc chụp màn hình file secret."

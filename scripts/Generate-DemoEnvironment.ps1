param(
    [string[]]$MachineNames = @("MAY-01", "MAY-02"),
    [decimal]$HourlyRate = 10000
)

$ErrorActionPreference = "Stop"

function New-RandomBase64([int]$ByteCount = 32) {
    $bytes = [byte[]]::new($ByteCount)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

$machineKeys = [ordered]@{}
foreach ($machineName in $MachineNames) {
    if ([string]::IsNullOrWhiteSpace($machineName)) {
        throw "Tên máy không được để trống."
    }

    $machineKeys[$machineName.Trim()] = New-RandomBase64
}

$otpSecret = New-RandomBase64
$machineKeysJson = $machineKeys | ConvertTo-Json -Compress

Write-Host "Chạy các lệnh sau trong PowerShell dùng để khởi động Server:"
Write-Host ""
Write-Host ('$env:NETCAFE_OTP_HASH_SECRET_BASE64 = ''{0}''' -f $otpSecret)
Write-Host ('$env:NETCAFE_MACHINE_KEYS_JSON = ''{0}''' -f $machineKeysJson)
Write-Host ('$env:NETCAFE_HOURLY_RATE = ''{0}''' -f $HourlyRate)
Write-Host '$env:NETCAFE_DB_CONNECTION = ''Server=localhost;Database=NetCafeManager;Trusted_Connection=True;TrustServerCertificate=True;'''
Write-Host '$env:NETCAFE_SMTP_HOST = ''smtp.example.com'''
Write-Host '$env:NETCAFE_SMTP_PORT = ''587'''
Write-Host '$env:NETCAFE_SMTP_SECURITY = ''StartTls'''
Write-Host '$env:NETCAFE_SMTP_USERNAME = ''replace-me'''
Write-Host '$env:NETCAFE_SMTP_PASSWORD = ''replace-me'''
Write-Host '$env:NETCAFE_SMTP_FROM = ''replace-me@example.com'''
Write-Host '$env:NETCAFE_SMTP_FROM_NAME = ''NetCafeManager'''
Write-Host '$env:NETCAFE_TCP_PORT = ''5050'''
Write-Host ""
Write-Warning "Không commit, chụp màn hình hoặc gửi các secret vừa tạo."

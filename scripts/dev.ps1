$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not (Test-Path '.env')) {
    Copy-Item '.env.example' '.env'
    Write-Host 'Создан файл .env. Заполните POSTGRES_PASSWORD и LOCALSIEM_ADMIN_PASSWORD и запустите скрипт снова.' -ForegroundColor Yellow
    exit 1
}

$settings = @{}
foreach ($line in Get-Content '.env') {
    if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        $settings[$Matches[1]] = $Matches[2].Trim()
    }
}

function Get-Setting([string]$name, [string]$default) {
    if ($settings.ContainsKey($name) -and $settings[$name]) { return $settings[$name] }
    return $default
}

$password = Get-Setting 'POSTGRES_PASSWORD' ''
if (-not $password) { throw 'В .env не задан POSTGRES_PASSWORD' }

$jwtKey = Get-Setting 'LOCALSIEM_JWT_KEY' ''
if (-not $jwtKey) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $jwtKey = [Convert]::ToBase64String($bytes)
    (Get-Content '.env') -replace '^LOCALSIEM_JWT_KEY=.*$', "LOCALSIEM_JWT_KEY=$jwtKey" | Set-Content '.env'
    Write-Host 'Сгенерирован LOCALSIEM_JWT_KEY и сохранён в .env'
}

docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Не удалось запустить PostgreSQL через Docker Compose' }

$db = Get-Setting 'POSTGRES_DB' 'localsiem'
$user = Get-Setting 'POSTGRES_USER' 'localsiem'
$port = Get-Setting 'POSTGRES_PORT' '5432'
$env:ConnectionStrings__Postgres = "Host=localhost;Port=$port;Database=$db;Username=$user;Password=$password"
$env:Jwt__Key = $jwtKey
$adminPassword = Get-Setting 'LOCALSIEM_ADMIN_PASSWORD' ''
if ($adminPassword) { $env:Seed__AdminPassword = $adminPassword }

if (-not (Test-Path 'src/LocalSIEM.Infrastructure/Persistence/Migrations')) {
    & (Join-Path $PSScriptRoot 'create-migration.ps1')
}

if (-not (Test-Path 'src/LocalSIEM.Api/wwwroot/index.html')) {
    npm --prefix web install
    npm --prefix web run build
}

Write-Host 'LocalSIEM: http://localhost:5080' -ForegroundColor Green
dotnet run --project src/LocalSIEM.Api --launch-profile LocalSIEM.Api

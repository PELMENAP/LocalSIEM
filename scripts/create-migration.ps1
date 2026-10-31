param([string]$Name = 'InitialCreate')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet tool restore
dotnet ef migrations add $Name `
    --project src/LocalSIEM.Infrastructure `
    --startup-project src/LocalSIEM.Api `
    --output-dir Persistence/Migrations
if ($LASTEXITCODE -ne 0) { throw 'Не удалось создать миграцию' }

param(
    [Parameter(Mandatory = $true)][string]$ApiKey,
    [string]$Url = 'http://localhost:5080',
    [string]$SourceIp = '192.0.2.44',
    [string]$Username = 'buh-ivanova',
    [int]$Count = 6
)
$ErrorActionPreference = 'Stop'
$now = [DateTimeOffset]::UtcNow
$events = for ($i = 0; $i -lt $Count; $i++) {
    @{
        timestamp = $now.AddSeconds($i).ToString('o')
        eventType = 'LoginFailed'
        sourceIp  = $SourceIp
        targetIp  = '10.10.0.2'
        username  = $Username
        hostname  = 'EXT-CLIENT'
        message   = 'Неверный пароль (внешнее приложение)'
        attributes = @{ application = 'crm-portal' }
        raw       = @{ origin = 'send-events.ps1'; attempt = $i + 1 }
    }
}
$body = @{ events = $events } | ConvertTo-Json -Depth 6
$response = Invoke-RestMethod -Method Post -Uri "$Url/api/ingest/events" -Headers @{ 'X-Api-Key' = $ApiKey } -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
Write-Host "Принято событий: $($response.accepted)"

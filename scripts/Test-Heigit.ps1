$ErrorActionPreference = 'Stop'
$VerbosePreference = 'SilentlyContinue'
$DebugPreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Import-LocalEnvironment.ps1')
Import-HoodiLocalEnvironment
if ([string]::IsNullOrWhiteSpace($env:Rotas__ApiKey)) { throw 'Preencha Rotas__ApiKey no .env antes do teste.' }
# Fixed public synthetic coordinates. No private journey or key enters the URL/output.
$body = @{
    coordinates = @(@(-46.62905519645476, -23.55937804586215), @(-46.63206653567886, -23.55770344834097))
    instructions = $false
    alternative_routes = @{ target_count = 3; weight_factor = 1.4; share_factor = 0.6 }
} | ConvertTo-Json -Depth 5 -Compress
$headers = @{ Authorization = $env:Rotas__ApiKey }
try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri 'https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson' -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 20 -MaximumRedirection 0
} catch {
    $status = 'sem resposta HTTP'
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    # Never print exception details: upstream errors can echo request context.
    Write-Output "Teste HeiGIT falhou: $status. Chave e resposta omitidas."
    exit 1
} finally {
    $headers.Clear()
    [Environment]::SetEnvironmentVariable('Rotas__ApiKey', $null, 'Process')
}
$formatFailure = 'JSON invalido'
try {
    # application/geo+json can be returned as byte[] by PowerShell's web cmdlet.
    $jsonText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
    $data = $jsonText | ConvertFrom-Json
    $features = @($data.features)
    $formatFailure = "quantidade de rotas recebidas: $($features.Count)"
    if ($features.Count -lt 1 -or $features.Count -gt 3) { throw 'Unexpected route count' }
    $formatFailure = 'geometria ou duracao invalida'
    foreach ($feature in $features) {
        if ($feature.geometry.type -ne 'LineString' -or @($feature.geometry.coordinates).Count -lt 2 -or $feature.properties.summary.duration -le 0) { throw 'Invalid route' }
    }
    Write-Output "HeiGIT HTTP $($response.StatusCode): $($features.Count) rota(s) a pe validas. Chave e geometria omitidas."
} catch {
    Write-Output "HeiGIT HTTP $($response.StatusCode): $formatFailure. Chave e resposta omitidas."
    exit 1
}

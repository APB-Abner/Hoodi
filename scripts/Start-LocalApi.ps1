$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Import-LocalEnvironment.ps1')
Import-HoodiLocalEnvironment
# ASP.NET does not load .env automatically. Only this process receives the values.
dotnet run --project (Join-Path $PSScriptRoot '../Hoodi.Api') -- --environment Development
exit $LASTEXITCODE

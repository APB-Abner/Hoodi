function Import-HoodiLocalEnvironment {
    param([string]$EnvFile = (Join-Path $PSScriptRoot '../.env'))
    if (!(Test-Path -LiteralPath $EnvFile -PathType Leaf)) { throw 'Arquivo .env local nao encontrado.' }
    $allowed = @('Rotas__ApiKey', 'Rotas__FonteRisco', 'Rotas__SemDado')
    $seen = @{}
    foreach ($line in [System.IO.File]::ReadAllLines([System.IO.Path]::GetFullPath($EnvFile))) {
        $text = $line.Trim()
        if (!$text -or $text.StartsWith('#')) { continue }
        $separator = $text.IndexOf('=')
        if ($separator -lt 1) { throw 'Linha invalida no .env. Use NOME=valor.' }
        $name = $text.Substring(0, $separator).Trim()
        if ($name -notin $allowed) { throw 'O .env local aceita apenas as tres configuracoes Rotas documentadas.' }
        if ($seen.ContainsKey($name)) { throw 'Configuracao duplicada no .env.' }
        $seen[$name] = $true
        $value = $text.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}

param([Parameter(ValueFromRemainingArguments = $true)][string[]]$GitArguments)
$ErrorActionPreference = 'Stop'
# Some Git commands reject the workstation's empty gpg.format setting before
# processing command-line overrides. Use a temporary corrected copy only for
# this invocation; the user's actual configuration is never modified.
$taskGlobalConfig = Join-Path $env:USERPROFILE '.gitconfig'
$taskTemporaryConfig = Join-Path ([IO.Path]::GetTempPath()) ('J5-release-git-' + [guid]::NewGuid().ToString('N') + '.config')
$taskPreviousConfig = $env:GIT_CONFIG_GLOBAL
try {
    $taskText = [IO.File]::ReadAllText($taskGlobalConfig)
    $taskSection = ''
    $taskLines = foreach ($taskLine in ($taskText -split '\r?\n')) {
        if ($taskLine -match '^\s*\[([^\]]+)\]') { $taskSection = $Matches[1] }
        if ($taskSection -eq 'gpg' -and $taskLine -match '^\s*format\s*=\s*$') { '    format = openpgp' }
        else { $taskLine }
    }
    [IO.File]::WriteAllLines($taskTemporaryConfig, [string[]]$taskLines)
    $env:GIT_CONFIG_GLOBAL = $taskTemporaryConfig
    & git @GitArguments
    $taskExit = $LASTEXITCODE
}
finally {
    $env:GIT_CONFIG_GLOBAL = $taskPreviousConfig
    if (Test-Path -LiteralPath $taskTemporaryConfig) { Remove-Item -LiteralPath $taskTemporaryConfig -Force }
}
exit $taskExit

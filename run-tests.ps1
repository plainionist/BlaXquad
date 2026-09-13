$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$logDirectory = Join-Path $PSScriptRoot '.tmp-tests'
$logPath = Join-Path $logDirectory 'latest-test.log'

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

try {
    & dotnet test @args *> $logPath
    $testExitCode = $LASTEXITCODE
}
catch {
    $_ | Out-File -LiteralPath $logPath -Append
    $testExitCode = 1
}

if ($testExitCode -eq 0) {
    Write-Output 'TEST_RESULT status=passed'
}
else {
    Write-Output "TEST_RESULT status=failed exitCode=$testExitCode log=$logPath"
}

exit $testExitCode
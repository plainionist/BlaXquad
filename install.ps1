$ErrorActionPreference = 'Stop'
$rootDirectory = $PSScriptRoot
$uiDirectory = Join-Path $rootDirectory 'src\squad-ui'

$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$is64BitOperatingSystem = [System.Environment]::Is64BitOperatingSystem
if (-not $isWindowsPlatform -or -not $is64BitOperatingSystem) {
    $platformDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    throw "Unsupported platform: $platformDescription. Use install.sh on Linux or macOS."
}

$rid = 'win-x64'
$outputDirectory = Join-Path $rootDirectory "bin"
if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory | Out-Null

& npm.cmd ci --prefix $uiDirectory
if ($LASTEXITCODE -ne 0) {
    throw "npm ci failed with exit code $LASTEXITCODE."
}
& npm.cmd run build --prefix $uiDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Vue build failed with exit code $LASTEXITCODE."
}

$publishArguments = @(
    '--configuration', 'Release',
    '--runtime', $rid,
    '--self-contained', 'true',
    "--property:PublishDir=$outputDirectory\",
    '--nologo'
)
& dotnet publish (Join-Path $rootDirectory 'src\squad\squad.csproj') @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Publishing squad failed with exit code $LASTEXITCODE."
}
& dotnet publish (Join-Path $rootDirectory 'src\squad-hq\squad-hq.csproj') @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Publishing squad-hq failed with exit code $LASTEXITCODE."
}

$expectedFiles = @(
    'ui\index.html',
    'squad.exe',
    'squad-hq.exe',
    'Photino.Native.dll',
    "runtimes\$rid\native\copilot.exe",
    "runtimes\$rid\native\copilot_runtime.dll"
)
foreach ($relativePath in $expectedFiles) {
    $path = Join-Path $outputDirectory $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Publish output is missing: $path"
    }
}

Write-Output "BlaXquad installed in $outputDirectory"
Write-Output 'Add this directory to PATH.'

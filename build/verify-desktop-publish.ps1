<#
.SYNOPSIS
    Verifies a published portable BidParser desktop build before it is released.

.DESCRIPTION
    Asserts the release contract the plan sets out: the publish directory is exactly one
    BidParser.exe, its Windows version resource carries the version that was tagged, and the
    executable starts without crashing.

    The version resource check also guards against releasing an executable published from a
    non-Windows machine. Cross-compiling win-x64 from Linux produces a structurally valid
    single-file exe, but the SDK cannot rewrite the apphost's Win32 resources off Windows, so it
    keeps Microsoft's .NET host ProductVersion and default icon. This script fails that build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $ExpectedVersion,
    [int] $LaunchSeconds = 10
)

$ErrorActionPreference = 'Stop'

function Fail([string] $message) {
    Write-Host "::error::$message"
    exit 1
}

if (-not (Test-Path -LiteralPath $PublishDir)) {
    Fail "Publish directory '$PublishDir' does not exist."
}

# 1. Exactly one file, and it is the executable.
$files = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File)
$names = ($files | ForEach-Object { $_.Name }) -join ', '
if ($files.Count -ne 1) {
    Fail "Expected exactly one published file, found $($files.Count): $names"
}
if ($files[0].Name -ne 'BidParser.exe') {
    Fail "Expected BidParser.exe, found $($files[0].Name)"
}

$exe = $files[0].FullName
Write-Host "Published one file: BidParser.exe ($([math]::Round($files[0].Length / 1MB, 1)) MB)"

# 2. Version metadata must match the tag exactly. FileVersion takes the SemVer core because a
#    Windows FILEVERSION resource cannot carry a prerelease label.
$expectedCore = ($ExpectedVersion -split '[-+]')[0]
$info = (Get-Item -LiteralPath $exe).VersionInfo

if ($info.ProductVersion -ne $ExpectedVersion) {
    Fail "ProductVersion is '$($info.ProductVersion)', expected '$ExpectedVersion'. An executable published off Windows keeps the .NET host's own resource and must not be released."
}
if ($info.FileVersion -ne "$expectedCore.0") {
    Fail "FileVersion is '$($info.FileVersion)', expected '$expectedCore.0'."
}
Write-Host "Version resource: ProductVersion=$($info.ProductVersion) FileVersion=$($info.FileVersion)"

# 3. Smoke launch. A startup failure — a missing runtime asset, an unresolvable XAML resource, a
#    broken bundled configuration document — exits immediately, which is exactly what this catches.
$launchStarted = Get-Date
$process = Start-Process -FilePath $exe -PassThru
try {
    if ($process.WaitForExit($LaunchSeconds * 1000)) {
        $events = @(Get-WinEvent -FilterHashtable @{
            LogName = 'Application'
            StartTime = $launchStarted.AddSeconds(-2)
        } -ErrorAction SilentlyContinue | Where-Object {
            $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting') -and
            $_.Message -match 'BidParser'
        } | Select-Object -First 5)
        foreach ($event in $events) {
            Write-Host "::error::Windows application event ($($event.ProviderName)): $($event.Message)"
        }
        Fail "BidParser.exe exited after $($process.ExitCode) within $LaunchSeconds seconds of launching."
    }

    $process.Refresh()
    if ($process.MainWindowHandle -eq 0) {
        # Not fatal: a hosted runner may deny the process an interactive desktop. Staying alive is
        # the assertion that matters.
        Write-Host "::warning::No main window handle was reported; the process is running but its window could not be observed."
    }
    else {
        Write-Host "Main window opened."
    }
}
finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (-not $process.HasExited) { $process.Kill() }
    }
}

# 4. The hash published in the release body so a user can verify their download.
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "SHA-256: $hash"
if ($env:GITHUB_OUTPUT) {
    "sha256=$hash" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

Write-Host "Portable desktop build verified."

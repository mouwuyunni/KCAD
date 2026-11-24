param([string] $CoreConsole)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$CoreConsole) {
    foreach ($series in 'R23.1', 'R23.0') {
        $products = Get-ItemProperty "HKLM:\SOFTWARE\Autodesk\AutoCAD\$series\*" -ErrorAction SilentlyContinue
        foreach ($product in $products) {
            if (!$product.AcadLocation) { continue }
            $candidate = Join-Path $product.AcadLocation 'accoreconsole.exe'
            if (Test-Path -LiteralPath $candidate) {
                $CoreConsole = $candidate
                break
            }
        }
        if ($CoreConsole) { break }
    }
}
if (!$CoreConsole) {
    $command = Get-Command accoreconsole.exe -ErrorAction SilentlyContinue
    if ($command) { $CoreConsole = $command.Source }
}
if (!$CoreConsole -or !(Test-Path -LiteralPath $CoreConsole)) {
    throw 'AutoCAD Core Console was not found. Pass its executable path with -CoreConsole.'
}

$work = Join-Path $repo ('dist\regression-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$gbk = [Text.Encoding]::GetEncoding(936)
foreach ($folder in 'core', 'commands', 'test') {
    $destination = Join-Path $work $folder
    New-Item -Path $destination -ItemType Directory -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repo $folder) -File -Filter '*.lsp' | ForEach-Object {
        [IO.File]::WriteAllText((Join-Path $destination $_.Name), [IO.File]::ReadAllText($_.FullName), $gbk)
    }
}

$scriptPath = Join-Path $work 'regression.scr'
$log = Join-Path $work 'regression.log'
$lines = @(
    '(setq rc:test-trust (getvar "TRUSTEDPATHS"))'
    '(setvar "TRUSTEDPATHS" (strcat rc:test-trust ";" (getenv "RC_ROOT") "\\..."))'
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'run_tests.scr') -Encoding UTF8 | Where-Object { $_ -ne '(quit)' }
    '(setvar "TRUSTEDPATHS" rc:test-trust)'
    '_.QUIT'
    '_Yes'
    ''
)
[IO.File]::WriteAllLines($scriptPath, [string[]]$lines, $gbk)

$oldRoot = $env:RC_ROOT
try {
    $env:RC_ROOT = $work
    $options = @{
        FilePath = $CoreConsole
        ArgumentList = @('/s', ('"' + $scriptPath + '"'))
        WindowStyle = 'Hidden'
        PassThru = $true
        RedirectStandardOutput = $log
    }
    $process = Start-Process @options
    if (!$process.WaitForExit(60000)) {
        Stop-Process -Id $process.Id
        throw "Core Console did not finish. See $log."
    }
} finally {
    $env:RC_ROOT = $oldRoot
}

$result = [IO.File]::ReadAllText($log, [Text.Encoding]::Unicode)
$result -split '\r?\n' | Where-Object { $_ -match '\[PASS\]|\[FAIL\]|PASS=\d+' }
if ($result -notmatch 'PASS=47\s+FAIL=0' -or $result -notmatch '(?m)^RC_TEST_FINISHED') {
    throw "RuiCAD regression failed. See $log."
}
Write-Host "All 47 legacy cases passed. Log: $log"

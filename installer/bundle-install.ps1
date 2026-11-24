param([switch] $Uninstall)

$ErrorActionPreference = 'Stop'
if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    throw 'Close AutoCAD before installing or uninstalling KCAD.'
}

$autodesk = Join-Path $env:APPDATA 'Autodesk'
$plugins = Join-Path $autodesk 'ApplicationPlugins'
$destination = Join-Path $plugins 'RuiCAD.bundle'
$trustedPath = (Join-Path $destination 'Contents') + '\...'

function Update-TrustedPaths {
    $registry = 'HKCU:\Software\Autodesk\AutoCAD'
    foreach ($series in 'R23.0', 'R23.1', 'R24.0', 'R24.1', 'R24.2', 'R24.3') {
        $release = Join-Path $registry $series
        if (!(Test-Path -LiteralPath $release)) { continue }
        foreach ($product in Get-ChildItem -LiteralPath $release | Where-Object PSChildName -Like 'ACAD-*:*') {
            $profiles = Join-Path $product.PSPath 'Profiles'
            if (!(Test-Path -LiteralPath $profiles)) { continue }
            foreach ($profile in Get-ChildItem -LiteralPath $profiles) {
                $variables = Join-Path $profile.PSPath 'Variables'
                if (!(Test-Path -LiteralPath $variables)) { continue }
                $current = Get-ItemProperty -LiteralPath $variables -Name TRUSTEDPATHS -ErrorAction SilentlyContinue
                $paths = @([string]$current.TRUSTEDPATHS -split ';' | Where-Object { $_.Trim() })
                $newPaths = @($paths | Where-Object { $_.Trim().Trim('"') -ne $trustedPath })
                if (!$Uninstall) { $newPaths += $trustedPath }
                if (($newPaths -join ';') -ne ($paths -join ';')) {
                    New-ItemProperty -LiteralPath $variables -Name TRUSTEDPATHS -PropertyType String -Value ($newPaths -join ';') -Force | Out-Null
                }
            }
        }
    }
}

if (Test-Path -LiteralPath $destination) {
    $existing = Get-Item -LiteralPath $destination -Force
    if (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        $existing.FullName -ne (Join-Path (Resolve-Path -LiteralPath $plugins).Path 'RuiCAD.bundle')) {
        throw 'Unexpected KCAD installation path.'
    }
}

if ($Uninstall) {
    if ($existing) {
        $library = Join-Path $existing.FullName 'Contents\library'
        if ((Test-Path -LiteralPath $library) -and (Get-ChildItem -LiteralPath $library -Recurse -File -Filter '*.dwg')) {
            $backup = Join-Path $autodesk ("RuiCAD-library-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
            Copy-Item -LiteralPath $library -Destination $backup -Recurse
            Write-Host "DWG library preserved at $backup"
        }
        Remove-Item -LiteralPath $existing.FullName -Recurse -Force
    }
    Update-TrustedPaths
    Write-Host 'KCAD uninstalled.'
    exit
}

$source = Join-Path $PSScriptRoot 'RuiCAD.bundle'
if (!(Test-Path -LiteralPath (Join-Path $source 'Contents\RuiCAD.UI.dll'))) {
    throw 'Extract the entire RuiCAD-CAD2020 zip first.'
}
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
& robocopy.exe $source $destination /E /R:0 /W:0 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "Could not copy KCAD (robocopy exit $LASTEXITCODE)." }
Update-TrustedPaths
Write-Host 'KCAD installed. Restart AutoCAD 2020 and open a drawing. Type KCAD to reopen the panel.'

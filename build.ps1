param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $RestoreSource
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\RuiCAD.UI\RuiCAD.UI.csproj'
$buildArgs = @($project, '/restore', '/m', '/nologo', "/p:Configuration=$Configuration", '/p:Platform=x64')
if ($RestoreSource) { $buildArgs += "/p:RestoreSources=$RestoreSource" }

$msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
if ($msbuild) {
    & $msbuild.Source @buildArgs
} else {
    & dotnet msbuild @buildArgs
}
if ($LASTEXITCODE -ne 0) { throw 'MSBuild failed.' }

$bin = Join-Path $root "src\RuiCAD.UI\bin\x64\$Configuration\net472"
$dll = Join-Path $bin 'RuiCAD.UI.dll'
if (!(Test-Path -LiteralPath $dll)) { throw "Missing output: $dll" }
foreach ($name in 'AcMgd.dll', 'AcDbMgd.dll', 'AcCoreMgd.dll') {
    if (Test-Path -LiteralPath (Join-Path $bin $name)) { throw "Unexpected AutoCAD SDK file: $name" }
}

$output = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$staging = Join-Path $output ([Guid]::NewGuid().ToString('N'))
$bundle = Join-Path $staging 'RuiCAD.bundle'
$contents = Join-Path $bundle 'Contents'
New-Item -ItemType Directory -Path $contents -Force | Out-Null
try {
    Copy-Item -LiteralPath $dll -Destination $contents
    Copy-Item -LiteralPath (Join-Path $root 'installer\PackageContents.xml') -Destination $bundle
    foreach ($name in 'install.bat', 'uninstall.bat', 'bundle-install.ps1') {
        Copy-Item -LiteralPath (Join-Path $root "installer\$name") -Destination $staging
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'docs\CAD2020.md') -Destination $staging

    # AutoCAD 2020 uses the Windows legacy code page for AutoLISP sources.
    $gbk = [Text.Encoding]::GetEncoding(936)
    foreach ($name in 'init.lsp', 'kcad-bridge.lsp') {
        [IO.File]::WriteAllText((Join-Path $contents $name), [IO.File]::ReadAllText((Join-Path $root $name)), $gbk)
    }
    foreach ($folder in 'core', 'commands') {
        $destination = Join-Path $contents $folder
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Filter '*.lsp' | ForEach-Object {
            [IO.File]::WriteAllText((Join-Path $destination $_.Name), [IO.File]::ReadAllText($_.FullName), $gbk)
        }
    }
    Copy-Item -LiteralPath (Join-Path $root 'library') -Destination $contents -Recurse -Force
    $zip = Join-Path $output 'RuiCAD-CAD2020.zip'
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
    Write-Host "Created $zip"
} finally {
    $resolved = (Resolve-Path -LiteralPath $staging).Path
    if ((Split-Path -Parent $resolved) -eq (Resolve-Path -LiteralPath $output).Path) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

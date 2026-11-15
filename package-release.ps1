#requires -version 5.1
<#
    打包发布用的压缩包：exe + README.md + LICENSE，并生成 SHA256 校验文件。
    用法：pwsh -File .\package-release.ps1
    产物（都在 dist\ 下，已被 .gitignore 忽略）：
      PCL-Update-Blocker-<版本>-win.zip
      PCL-Update-Blocker-<版本>-win.zip.sha256
    只负责打包，不涉及 git、不联网、不修改仓库文件。
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

$exePath = Join-Path $OutputDirectory 'PCL-Update-Blocker.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "找不到 $exePath，请先运行 .\build.ps1"
}

$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
# ProductVersion 来自 AssemblyInformationalVersion（1.0.0），比 FileVersion（1.0.0.0）更适合做包名。
$version = $info.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = $info.FileVersion }
$packageName = "PCL-Update-Blocker-$version-win"
$zipPath = Join-Path $OutputDirectory ($packageName + '.zip')
$hashPath = $zipPath + '.sha256'

$staging = Join-Path ([IO.Path]::GetTempPath()) ('pcl-blocker-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    Copy-Item -LiteralPath $exePath -Destination $staging
    foreach ($name in @('README.md', 'LICENSE')) {
        $source = Join-Path $PSScriptRoot $name
        if (-not (Test-Path -LiteralPath $source)) { throw "缺少 $source" }
        Copy-Item -LiteralPath $source -Destination $staging
    }

    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal

    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $packageName.zip"
    [IO.File]::WriteAllText($hashPath, $line + "`r`n", (New-Object Text.UTF8Encoding($false)))

    Write-Host "版本: $version"
    Write-Host "压缩包: $zipPath"
    Write-Host "SHA256: $line"
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

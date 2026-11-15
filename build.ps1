#requires -version 5.1
<#
    编译 PCL-Update-Blocker.exe（.NET Framework 4.x + WinForms，无需安装 SDK）。
    用法：pwsh -File .\build.ps1（Windows PowerShell 5.1 亦可）

    src\ 下的所有 .cs 都会被编译，包括 src\AssemblyInfo.cs，
    因此版本号、产品名等元数据只需在 AssemblyInfo.cs 里维护一份。
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

function Get-CscPath {
    $candidates = @(
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
        'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw '找不到 .NET Framework 4.x 的 csc.exe，请确认系统已安装 .NET Framework 4.x。'
}

$csc = Get-CscPath
$sourceDirectory = Join-Path $PSScriptRoot 'src'
$sources = Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' | Sort-Object Name
if (-not $sources) { throw "没有找到源文件：$sourceDirectory" }

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$outputPath = Join-Path $OutputDirectory 'PCL-Update-Blocker.exe'

$arguments = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/utf8output'
    '/warn:4'
    ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest'))
    '/reference:System.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    ('/out:' + $outputPath)
) + ($sources | ForEach-Object { $_.FullName })

Write-Host "编译器: $csc"
Write-Host "输出:   $outputPath"

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "编译失败，csc 退出码 $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $outputPath)) { throw "编译没有生成 $outputPath" }

$file = Get-Item -LiteralPath $outputPath
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($outputPath)
Write-Host ("版本: {0} ({1})" -f $version.FileVersion, $version.ProductName)
Write-Host ("完成: {0} ({1:N0} 字节, 修改时间 {2:yyyy-MM-dd HH:mm:ss})" -f $file.FullName, $file.Length, $file.LastWriteTime)

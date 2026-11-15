#requires -version 5.1
<#
    编译并运行全部夹具测试：
      1) 核心逻辑测试（tests\TestMain.cs + src\SetupIniEditor.cs + src\LauncherTarget.cs）
      2) 真实 exe 集成测试（--apply 命令行模式、拖到 exe 图标上的 GUI 冒烟）
    所有夹具都在系统临时目录，结束时删除；不会触碰真实的 PCL 安装。
    用法：pwsh -File .\tests\run-tests.ps1 [-SkipGuiSmoke]
#>
[CmdletBinding()]
param(
    [switch] $SkipGuiSmoke
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ('pcl-blocker-tests-' + [guid]::NewGuid().ToString('N'))
$script:failures = 0
$script:checks = 0

function Get-CscPath {
    foreach ($candidate in @(
            'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
            'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe')) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw '找不到 .NET Framework 4.x 的 csc.exe。'
}

function Write-Check {
    param([string] $Name, [bool] $Ok, [string] $Detail = '')
    $script:checks++
    if ($Ok) {
        Write-Host "PASS $Name"
    }
    else {
        $script:failures++
        Write-Host "FAIL $Name $Detail" -ForegroundColor Red
    }
}

function New-Fixture {
    param([string] $Name)
    $dir = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Write-IniFile {
    param([string] $Path, [string] $Text)
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

function Invoke-Product {
    param([string] $ExePath, [string[]] $Arguments)
    $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -Wait -PassThru
    return $process.ExitCode
}

New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

try {
    Write-Host '=== 1. 编译产品 exe ==='
    & (Join-Path $projectRoot 'build.ps1') | Out-Null
    $productExe = Join-Path $projectRoot 'dist\PCL-Update-Blocker.exe'
    if (-not (Test-Path -LiteralPath $productExe)) { throw "构建后找不到 $productExe" }
    Write-Host "OK $productExe"

    Write-Host ''
    Write-Host '=== 2. 核心逻辑测试 ==='
    $csc = Get-CscPath
    $testExe = Join-Path $workRoot 'SetupIniEditor.Tests.exe'
    $testSources = @(
        (Join-Path $projectRoot 'src\SetupIniEditor.cs'),
        (Join-Path $projectRoot 'src\LauncherTarget.cs'),
        (Join-Path $PSScriptRoot 'TestMain.cs')
    )
    & $csc /nologo /target:exe /platform:anycpu /utf8output /warn:4 /reference:System.dll ('/out:' + $testExe) $testSources
    if ($LASTEXITCODE -ne 0) { throw "测试程序编译失败，csc 退出码 $LASTEXITCODE" }

    & $testExe
    $coreExit = $LASTEXITCODE
    Write-Check -Name '核心逻辑测试全部通过' -Ok ($coreExit -eq 0) -Detail "(退出码 $coreExit)"

    Write-Host ''
    Write-Host '=== 3. 真实 exe 集成测试 ==='

    # 3.1 --apply：标准位置
    $case = New-Fixture 'apply'
    New-Item -ItemType Directory -Path (Join-Path $case 'PCL') -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $case 'PCL.exe'), (New-Object byte[] 0))
    $ini = Join-Path $case 'PCL\Setup.ini'
    Write-IniFile -Path $ini -Text "Version:1`r`nSystemSystemUpdate:0`r`nSystemTheme:0`r`n"
    $exitCode = Invoke-Product -ExePath $productExe -Arguments @('--apply', (Join-Path $case 'PCL.exe'))
    $content = [IO.File]::ReadAllText($ini)
    Write-Check -Name '--apply 退出码为 0' -Ok ($exitCode -eq 0) -Detail "(退出码 $exitCode)"
    Write-Check -Name '--apply 把键改为 3' -Ok ($content -match 'SystemSystemUpdate:3') -Detail $content
    Write-Check -Name '--apply 保留其它设置' -Ok ($content -match 'SystemTheme:0' -and $content -match 'Version:1')
    $backups = @(Get-ChildItem -LiteralPath (Join-Path $case 'PCL') -Filter 'Setup.ini.bak-*')
    Write-Check -Name '--apply 生成备份' -Ok ($backups.Count -eq 1) -Detail "(找到 $($backups.Count) 个备份)"

    # 3.2 --apply：缺少配置时失败且不创建文件
    $caseMissing = New-Fixture 'apply-missing'
    [IO.File]::WriteAllBytes((Join-Path $caseMissing 'PCL.exe'), (New-Object byte[] 0))
    $exitCode = Invoke-Product -ExePath $productExe -Arguments @('--apply', (Join-Path $caseMissing 'PCL.exe'))
    Write-Check -Name '缺少配置时退出码非 0' -Ok ($exitCode -ne 0) -Detail "(退出码 $exitCode)"
    Write-Check -Name '缺少配置时不创建任何文件' -Ok (-not (Test-Path -LiteralPath (Join-Path $caseMissing 'PCL'))) -Detail ''

    # 3.3 --apply：只读配置安全失败
    $caseReadOnly = New-Fixture 'apply-readonly'
    New-Item -ItemType Directory -Path (Join-Path $caseReadOnly 'PCL') -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $caseReadOnly 'PCL.exe'), (New-Object byte[] 0))
    $readOnlyIni = Join-Path $caseReadOnly 'PCL\Setup.ini'
    Write-IniFile -Path $readOnlyIni -Text "SystemSystemUpdate:0`r`n"
    (Get-Item -LiteralPath $readOnlyIni).Attributes = 'ReadOnly'
    try {
        $exitCode = Invoke-Product -ExePath $productExe -Arguments @('--apply', (Join-Path $caseReadOnly 'PCL.exe'))
        $content = [IO.File]::ReadAllText($readOnlyIni)
        Write-Check -Name '只读配置时退出码非 0' -Ok ($exitCode -ne 0) -Detail "(退出码 $exitCode)"
        Write-Check -Name '只读配置内容未变化' -Ok ($content -match 'SystemSystemUpdate:0') -Detail $content
        $backups = @(Get-ChildItem -LiteralPath (Join-Path $caseReadOnly 'PCL') -Filter 'Setup.ini.bak-*')
        Write-Check -Name '只读失败时没有备份' -Ok ($backups.Count -eq 0) -Detail "(找到 $($backups.Count) 个备份)"
    }
    finally {
        (Get-Item -LiteralPath $readOnlyIni).Attributes = 'Normal'
    }

    # 3.4 GUI 冒烟：把 PCL.exe 路径作为启动参数（等价于拖到 exe 图标上）
    if ($SkipGuiSmoke) {
        Write-Host 'SKIP GUI 冒烟（-SkipGuiSmoke）'
    }
    else {
        $caseGui = New-Fixture 'gui-smoke'
        New-Item -ItemType Directory -Path (Join-Path $caseGui 'PCL') -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $caseGui 'PCL.exe'), (New-Object byte[] 0))
        $guiIni = Join-Path $caseGui 'PCL\Setup.ini'
        Write-IniFile -Path $guiIni -Text "SystemSystemUpdate:0`r`n"
        $guiProcess = $null
        try {
            $guiProcess = Start-Process -FilePath $productExe -ArgumentList @((Join-Path $caseGui 'PCL.exe')) -PassThru
            $deadline = (Get-Date).AddSeconds(15)
            $updated = $false
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 200
                if (([IO.File]::ReadAllText($guiIni)) -match 'SystemSystemUpdate:3') { $updated = $true; break }
                if ($guiProcess.HasExited) { break }
            }
            Write-Check -Name 'GUI 启动后自动处理拖入的路径' -Ok $updated -Detail "(窗口进程是否退出: $($guiProcess.HasExited))"
        }
        catch {
            Write-Host "SKIP GUI 冒烟（无法启动窗口：$($_.Exception.Message)）" -ForegroundColor Yellow
        }
        finally {
            if ($guiProcess -and -not $guiProcess.HasExited) {
                Stop-Process -Id $guiProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
if ($script:failures -eq 0) {
    Write-Host "全部通过（$script:checks 项检查）" -ForegroundColor Green
    exit 0
}

Write-Host "失败 $($script:failures) 项（共 $script:checks 项检查）" -ForegroundColor Red
exit 1

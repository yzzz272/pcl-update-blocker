# PCL 更新设置工具（PCL-Update-Blocker）

免安装的 Windows 小工具：修改 PCL 的本地更新偏好，用于关闭启动器更新提示。当前版本：**v1.0.0**。

**本项目的代码与文档由 AI 生成。**

下载：[Releases](../../releases)

工具定位 `Setup.ini`，将 `SystemSystemUpdate` 设为 `3`，修改前备份，修改后校验。不启动或结束 PCL。

## 系统要求

- Windows 7 / 8 / 8.1 / 10 / 11（32 位或 64 位）。
- 已安装 .NET Framework 4.x。工具本身是 .NET Framework 4.x 的 WinForms 程序，不携带也不下载运行时：
  Windows 8 及更高版本通常已自带 4.x；Windows 7 原版只带 .NET Framework 3.5，需要自行安装 4.x，否则无法启动。

## 使用方法

1. 完全退出 PCL。工具只检查 PCL 是否在运行，不会替你结束进程。
2. 用下面任一方式指定启动器：
   - 把 `PCL.exe` 或它的快捷方式（`.lnk`）拖进窗口；
   - 把 `PCL.exe` / 快捷方式拖到 `PCL-Update-Blocker.exe` 的图标上；
   - 点击拖放区，手动选择文件；
   - 也可以直接拖入 `PCL\Setup.ini` 或 PCL 所在文件夹。
3. 窗口顶部显示结果摘要；点「显示详细信息」可看配置文件路径、编码、定位方式、备份位置和校验结果。

命令行（可选，主要给脚本用）：

```text
PCL-Update-Blocker.exe --apply "C:\path\to\PCL.exe"
```

退出码：`0` 成功或本来就正确，`1` 失败，`2` 检测到 PCL 正在运行。

## 写入的内容

`Setup.ini` 不是标准 INI，而是 PCL 自己的 `键:值` 冒号格式，所以最终写入的是这一行：

```text
SystemSystemUpdate:3
```

处理规则：

- 已有其它值（包括误写成 `SystemSystemUpdate=0` 的等号写法）时，改写成 `SystemSystemUpdate:3`；
- 同名键出现多次时，每一行生效的键都改成 `3`；
- 没有这个键时，在文件末尾新增一行；
- 已经是 `SystemSystemUpdate:3` 时不写入、不备份，只报告无需修改；
- 相似键（如 `SystemSystemUpdate2`）、注释行、其它设置、缩进、换行风格（CRLF/LF）与文件编码均保持原样。

支持 UTF-8（带或不带 BOM）、UTF-16 LE/BE（带 BOM）、ANSI/GBK；无法无损解码时拒绝修改。

## 配置文件位置

以启动器所在目录为基准，优先 `<启动器目录>\PCL\Setup.ini`，其次 `<启动器目录>\Setup.ini`（旧版布局）。
两者都不存在时不做任何修改，也不会创建文件；请先离线启动一次 PCL 让它生成配置。

## 备份与还原

写入前把原文件复制为同目录下的 `Setup.ini.bak-<时间戳>`，再写临时文件替换，写入后重新读取并核对文件内容与备份，
校验失败会尝试用备份还原。还原方法：把备份文件复制回 `Setup.ini`。

## 边界

工具只修改本地更新偏好：不修改启动器 exe，不恢复 PCL 已被移除的功能，也不保证每个 PCL 版本都遵守该设置。
PCL 更新或重置设置后该键可能被改回，需要再运行一次；修改后要重启 PCL 才会读取。

## 构建、测试与打包

需要 Windows 和系统自带的 .NET Framework 4.x 编译器，不需要 .NET SDK 或 NuGet 包：

```powershell
pwsh -File .\build.ps1             # 产物：dist\PCL-Update-Blocker.exe
pwsh -File .\tests\run-tests.ps1   # 含 GUI 冒烟；无桌面环境时加 -SkipGuiSmoke
pwsh -File .\package-release.ps1   # 产物：dist\PCL-Update-Blocker-<版本>-win.zip + .sha256
```

## 许可

[MIT](LICENSE)

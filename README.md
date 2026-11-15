# PCL 更新设置工具（PCL-Update-Blocker）

当前版本：**v1.0.0（首个版本）**，功能范围就是下面列出的这些。

把 PCL 启动器（`PCL.exe`）拖进窗口，工具会找到它的 `Setup.ini`，把 `SystemSystemUpdate`
设为 `3`，然后在结果里告诉你改了什么、备份在哪。免安装、单文件 exe，只改本地更新偏好这一个键。

- 只改一个键，改前自动备份，改后重新读取校验。
- 不启动 PCL、不结束进程、不改注册表 / 防火墙 / hosts、不改文件属性、不联网。
- 不修改 `PCL.exe` 等任何程序文件，只处理 `Setup.ini`。

下载：[Releases](../../releases)

## 系统要求

- Windows 7 / 8 / 8.1 / 10 / 11（32 位或 64 位）。
- **已安装 .NET Framework 4.x**。工具本身是 .NET Framework 4.x 的 WinForms 程序，
  不携带运行时、不下载运行时：
  - Windows 8 / 8.1 / 10 / 11 自带 4.x，可直接运行；
  - Windows 7 原版只带 .NET Framework 3.5，**需要自行安装 .NET Framework 4.x**
    （通常通过 Windows Update 或微软官方安装包），否则无法启动。

## 使用方法

1. **完全退出 PCL**。工具只检查 PCL 是否在运行，不会替你结束进程。
2. 任选一种方式指定 PCL：
   - 把 `PCL.exe` 或它的快捷方式（`.lnk`）拖进窗口；
   - 把 `PCL.exe` / 快捷方式拖到 `PCL-Update-Blocker.exe` 的图标上；
   - 点击「选择 PCL 启动器」手动选择文件；
   - 也可以直接拖入 `PCL\Setup.ini` 或 PCL 所在文件夹。
3. 窗口顶部显示结果摘要；点「显示详细信息」可以看到配置文件路径、编码、定位方式、
   备份位置和校验结果。

命令行（可选，主要给脚本用）：

```text
PCL-Update-Blocker.exe --apply "C:\path\to\PCL.exe"
```

退出码：`0` 成功或本来就正确，`1` 失败，`2` 检测到 PCL 正在运行。

## `SystemSystemUpdate:3` 是什么意思

PCL 把「启动器更新偏好」保存在 `Setup.ini` 的 `SystemSystemUpdate` 键里。
这个文件不是标准 INI，而是 PCL 自己的 `键:值` 冒号格式，所以最终写入的是这一行：

```text
SystemSystemUpdate:3
```

`3` 就是 PCL 里「不提示 / 不自动检查启动器更新」的那一档。需要注意：

- 该值对应的下拉选项文字会随 PCL 版本变化，工具只写 `3` 这一个值，不做其它推断。
- 工具**只改这一个键**，不修改 `PCL.exe`，也不恢复 PCL 已被移除的功能。
- **不保证每个 PCL 版本都遵守这个偏好**；PCL 自身更新或重置设置后，该键可能被改回，需要再运行一次。

具体处理规则：

| 情况 | 处理 |
| --- | --- |
| 存在 `SystemSystemUpdate:0`（或其它值） | 改成 `SystemSystemUpdate:3` |
| 存在 `SystemSystemUpdate=0` 等号写法 | 归一化成 `SystemSystemUpdate:3` |
| 同名键出现多次 | 每一行生效的键都改成 `3` |
| 没有这个键 | 在文件末尾新增一行 `SystemSystemUpdate:3` |
| 已经是 `SystemSystemUpdate:3` | 不写入、不备份，只报告「无需修改」 |
| `SystemSystemUpdate2`、`SystemSystemUpdateNote` 等相似键 | 不动 |
| 以 `#` 或 `;` 开头的注释行 | 不动 |
| 其它设置、缩进、换行风格（CRLF/LF）、编码 | 原样保留 |

编码支持 UTF-8（带/不带 BOM）、UTF-16 LE/BE（带 BOM）、ANSI/GBK。
解码必须无损才会写入：解码失败、或「解码再编码」后与原文件字节不一致、或没有 BOM 却含 NUL 字节，
都会直接报错并且**不改文件**。

配置文件定位顺序（以拖入的 `PCL.exe` 所在目录为基准）：

1. `<启动器目录>\PCL\Setup.ini` —— PCL 的标准位置，优先使用；
2. `<启动器目录>\Setup.ini` —— 旧版布局，仅在标准位置不存在时使用；
3. 两者都不存在 —— 不做任何修改，并提示先离线启动一次 PCL 生成配置。

## 备份与还原

- 写入前把原文件完整复制为同目录下的 `Setup.ini.bak-<时间戳>`（时间戳重复时追加序号）。
- 先写同目录临时文件再替换，写入过程不会截断原文件。
- 写入后重新读取文件，比对「写入内容 vs 预期内容」和「备份 vs 原文件」；校验不通过会尝试用备份还原，
  并在提示里说明还原结果。
- **还原**：把备份文件复制回 `Setup.ini` 即可。

## 限制

- 只修改本地更新偏好，不能恢复已被移除的功能，也不能保证忽略该设置的版本一定不更新。
- 界面上的「完成」只代表配置文件内容已核对，不代表 PCL 一定遵守该设置；需要重启 PCL 后才会读取。
- 只读文件会直接报错（工具不会改文件属性）；权限不足时请把 PCL 放在有写权限的目录。
- 若 PCL 并未运行却提示「正在运行」，说明有同路径进程残留，请手动确认后重试。

## 构建

需要 Windows 和系统自带的 .NET Framework 4.x 编译器，**不需要** .NET SDK 或 NuGet 包：

```powershell
pwsh -File .\build.ps1
```

产物：`dist\PCL-Update-Blocker.exe`（`dist\` 已被 `.gitignore` 忽略，发布时单独上传）。
版本号、产品名等元数据只在 `src\AssemblyInfo.cs` 里维护，编译脚本会自动带上并打印版本。

脚本本身兼容 Windows PowerShell 5.1 与 PowerShell 7；`.ps1` 文件保存为 UTF-8 带 BOM，
以便 5.1 正确读取中文。

## 测试

```powershell
pwsh -File .\tests\run-tests.ps1                  # 含 GUI 冒烟
pwsh -File .\tests\run-tests.ps1 -SkipGuiSmoke    # 无桌面环境（如 CI）时跳过 GUI 冒烟
```

覆盖：冒号键替换、等号写法归一化、插入、重复键、相似键与注释、编码与换行保留、中文/空格路径、
只读与缺失文件的安全失败、备份唯一性、快捷方式解析、标准/旧版位置选择，以及真实 exe 的
`--apply` 与 GUI 启动路径。夹具全部在系统临时目录，运行结束自动删除，不会触碰真实的 PCL 安装。

GitHub Actions（`.github/workflows/build.yml`）在 `windows-latest` 上编译、跑夹具测试
（PowerShell 7 与 Windows PowerShell 5.1 各一次，跳过 GUI 冒烟），并上传 exe 作为构建产物。

## 发布打包（维护者）

```powershell
pwsh -File .\package-release.ps1
```

生成 `dist\PCL-Update-Blocker-<版本>-win.zip`（exe + README + LICENSE）及对应的 `.sha256`。

## 文件结构

```text
build.ps1                    编译脚本（csc，无外部依赖）
package-release.ps1          发布压缩包 + SHA256（可选）
src/Program.cs               入口：GUI 模式 + --apply 命令行模式
src/MainForm.cs              拖放窗口、中文界面与结果展示
src/SetupIniEditor.cs        核心：编码检测、按行改写、备份、原子替换、校验
src/LauncherTarget.cs        路径解析（exe / .lnk / Setup.ini / 文件夹）、运行中检测
src/AssemblyInfo.cs          版本与产品元数据（1.0.0）
src/app.manifest             asInvoker（不请求管理员权限）+ DPI 感知
tests/TestMain.cs            核心逻辑夹具测试
tests/run-tests.ps1          编译并运行全部测试 + 真实 exe 集成测试
.github/workflows/build.yml  CI：编译、测试、上传 exe
```

## 许可

[MIT](LICENSE)

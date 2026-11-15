using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PclUpdateBlocker
{
    public sealed class LauncherTarget
    {
        public string InputPath;
        public string LauncherExePath;
        public string SetupIniPath;
        public string SelectionNote;
        public bool IsDirectConfig;
    }

    public static class LauncherResolver
    {
        public const string CanonicalRelativePath = "PCL\\Setup.ini";

        public static LauncherTarget Resolve(string inputPath)
        {
            if (inputPath == null || inputPath.Trim().Length == 0)
                throw new SetupIniException("没有选择任何文件。请把 PCL 启动器拖进窗口。");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(inputPath.Trim().Trim('"'));
            }
            catch (Exception ex)
            {
                throw new SetupIniException("路径无效：" + inputPath, ex);
            }

            if (Directory.Exists(fullPath)) return ResolveFolder(fullPath);

            if (!File.Exists(fullPath))
                throw new SetupIniException("路径不存在：" + fullPath);

            string extension = Path.GetExtension(fullPath).ToLowerInvariant();

            if (extension == ".lnk")
            {
                string target = ResolveShortcutTarget(fullPath);

                if (Directory.Exists(target))
                {
                    LauncherTarget folderTarget = ResolveFolder(target);
                    folderTarget.InputPath = fullPath;
                    folderTarget.SelectionNote = "快捷方式指向文件夹 " + target + "；" + folderTarget.SelectionNote;
                    return folderTarget;
                }

                if (!File.Exists(target))
                    throw new SetupIniException("快捷方式指向的目标不存在：" + target + "\n请直接拖入 PCL.exe。");

                if (Path.GetExtension(target).ToLowerInvariant() != ".exe")
                    throw new SetupIniException("快捷方式指向的不是可执行文件：" + target + "\n请直接拖入 PCL.exe。");

                LauncherTarget exeTarget = ResolveExecutable(target);
                exeTarget.InputPath = fullPath;
                exeTarget.SelectionNote = "快捷方式指向 " + target + "；" + exeTarget.SelectionNote;
                return exeTarget;
            }

            if (extension == ".exe") return ResolveExecutable(fullPath);

            if (string.Equals(Path.GetFileName(fullPath), "Setup.ini", StringComparison.OrdinalIgnoreCase))
            {
                LauncherTarget configTarget = new LauncherTarget();
                configTarget.InputPath = fullPath;
                configTarget.SetupIniPath = fullPath;
                configTarget.IsDirectConfig = true;
                configTarget.SelectionNote = "直接使用选中的配置文件：" + fullPath;
                return configTarget;
            }

            throw new SetupIniException("不支持的文件类型：" + Path.GetFileName(fullPath)
                + "\n请拖入 PCL 启动器 (.exe)、它的快捷方式 (.lnk)，或 PCL\\Setup.ini。");
        }

        public static LauncherTarget ResolveExecutable(string exePath)
        {
            string directory = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(directory))
                throw new SetupIniException("无法确定启动器所在目录：" + exePath);

            string canonical = Path.Combine(Path.Combine(directory, "PCL"), "Setup.ini");
            string legacy = Path.Combine(directory, "Setup.ini");

            LauncherTarget target = new LauncherTarget();
            target.InputPath = exePath;
            target.LauncherExePath = exePath;

            if (File.Exists(canonical))
            {
                target.SetupIniPath = canonical;
                target.SelectionNote = "使用标准位置：" + canonical;
                return target;
            }

            if (File.Exists(legacy))
            {
                target.SetupIniPath = legacy;
                target.SelectionNote = "未找到 PCL\\Setup.ini，改用启动器目录下的旧版配置：" + legacy;
                return target;
            }

            throw new SetupIniException("未找到 PCL 配置文件，未做任何修改。\n已检查：\n  " + canonical
                + "\n  " + legacy
                + "\n\n请先离线启动一次 PCL 让它生成配置，或确认选择的 exe 确实是 PCL 启动器。"
                + "\n本工具不会凭空创建配置文件。");
        }

        public static LauncherTarget ResolveFolder(string folderPath)
        {
            string canonical = Path.Combine(Path.Combine(folderPath, "PCL"), "Setup.ini");
            string legacy = Path.Combine(folderPath, "Setup.ini");

            LauncherTarget target = new LauncherTarget();
            target.InputPath = folderPath;

            string guessExe = Path.Combine(folderPath, "PCL.exe");
            if (File.Exists(guessExe)) target.LauncherExePath = guessExe;

            if (File.Exists(canonical))
            {
                target.SetupIniPath = canonical;
                target.SelectionNote = "从文件夹解析到标准位置：" + canonical;
                return target;
            }

            if (File.Exists(legacy))
            {
                target.SetupIniPath = legacy;
                target.SelectionNote = "从文件夹解析到旧版配置：" + legacy;
                return target;
            }

            throw new SetupIniException("这个文件夹里没有找到 PCL 配置文件，未做任何修改。\n已检查：\n  " + canonical
                + "\n  " + legacy + "\n\n请拖入 PCL.exe 本身，或先离线启动一次 PCL 生成配置。");
        }

        /// <summary>
        /// 通过 WScript.Shell 读取 .lnk 的目标路径，只读不启动。
        /// </summary>
        public static string ResolveShortcutTarget(string shortcutPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    throw new SetupIniException("系统未提供 WScript.Shell，无法解析快捷方式。请直接拖入 PCL.exe。");

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { shortcutPath });

                if (shortcut == null)
                    throw new SetupIniException("无法读取快捷方式：" + shortcutPath);

                object targetPath = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
                    null, shortcut, null);
                string target = targetPath as string;

                if (string.IsNullOrEmpty(target))
                    throw new SetupIniException("快捷方式没有可用的目标路径：" + shortcutPath + "\n请直接拖入 PCL.exe。");

                return target;
            }
            catch (SetupIniException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SetupIniException("无法解析快捷方式：" + shortcutPath + "（" + ex.Message + "）\n请直接拖入 PCL.exe。", ex);
            }
            finally
            {
                ReleaseComObject(shortcut);
                ReleaseComObject(shell);
            }
        }

        /// <summary>
        /// 只做“识别”，不结束任何进程。无法可靠读取路径时按未运行处理。
        /// </summary>
        public static string FindRunningProcess(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;

            string fullExePath;
            try
            {
                fullExePath = Path.GetFullPath(exePath);
            }
            catch (Exception)
            {
                return null;
            }

            string processName = Path.GetFileNameWithoutExtension(fullExePath);
            if (string.IsNullOrEmpty(processName)) return null;

            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (Process process in processes)
            {
                try
                {
                    string runningPath = process.MainModule.FileName;
                    if (string.Equals(Path.GetFullPath(runningPath), fullExePath, StringComparison.OrdinalIgnoreCase))
                        return processName + ".exe（PID " + process.Id + "）";
                }
                catch (Exception)
                {
                    // 其他会话或更高权限的进程读不到路径，忽略。
                }
                finally
                {
                    process.Dispose();
                }
            }

            return null;
        }

        private static void ReleaseComObject(object instance)
        {
            if (instance == null) return;
            try
            {
                if (Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            }
            catch (Exception)
            {
                // 释放失败不影响结果。
            }
        }
    }
}

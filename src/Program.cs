using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PclUpdateBlocker
{
    internal static class Program
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        private static int Main(string[] args)
        {
            string path = null;
            bool applyMode = false;

            foreach (string argument in args)
            {
                if (string.Equals(argument, "--apply", StringComparison.OrdinalIgnoreCase))
                    applyMode = true;
                else if (path == null)
                    path = argument;
            }

            if (applyMode) return RunApply(path);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(path));
            return 0;
        }

        /// <summary>
        /// 命令行模式，供自动化测试和脚本使用：--apply &lt;PCL.exe|.lnk|Setup.ini&gt;
        /// 退出码：0 = 成功或本来就正确，1 = 失败，2 = 检测到 PCL 正在运行。
        /// </summary>
        private static int RunApply(string path)
        {
            StreamWriter writer = null;
            try
            {
                AttachConsole(AttachParentProcess);
                writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                writer.AutoFlush = true;
                Console.SetOut(writer);
            }
            catch (Exception)
            {
                // 没有可用控制台时保持静默，退出码仍然是有效结果。
                writer = null;
            }

            try
            {
                LauncherTarget target = LauncherResolver.Resolve(path);
                Console.WriteLine("配置文件：" + target.SetupIniPath);

                string running = LauncherResolver.FindRunningProcess(target.LauncherExePath);
                if (running != null)
                {
                    Console.WriteLine("失败：检测到 PCL 正在运行：" + running);
                    return 2;
                }

                SetupIniFileResult result = SetupIniEditor.UpdateFile(target.SetupIniPath);
                Console.WriteLine("结果：" + SetupIniEditor.DescribeChange(result.Change));
                if (!string.IsNullOrEmpty(result.BackupPath)) Console.WriteLine("备份：" + result.BackupPath);
                Console.WriteLine("校验：" + (result.Verified ? "通过" : "未执行"));
                return 0;
            }
            catch (SetupIniException ex)
            {
                Console.WriteLine("失败：" + ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("失败（未预期）：" + ex.Message);
                return 1;
            }
            finally
            {
                if (writer != null)
                {
                    try
                    {
                        writer.Flush();
                    }
                    catch (Exception)
                    {
                        // 控制台已关闭时忽略。
                    }
                }
            }
        }
    }
}

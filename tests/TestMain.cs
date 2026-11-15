using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using PclUpdateBlocker;

/// <summary>
/// 针对 SetupIniEditor / LauncherResolver 的夹具测试，由 tests\run-tests.ps1 编译并运行。
/// 所有夹具都放在系统临时目录，运行结束自动删除。
/// </summary>
internal static class TestMain
{
    private static int _passed;
    private static int _failed;
    private static string _currentTest = string.Empty;
    private static string _root = string.Empty;

    private static int Main()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcl-blocker-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        try
        {
            Run("冒号键替换并保留其它设置", TestColonReplace);
            Run("等号写法归一化为冒号", TestEqualTypo);
            Run("缺少键时在末尾插入", TestInsertKey);
            Run("文件末尾无换行时插入不改变风格", TestInsertWithoutTrailingNewline);
            Run("已经是 3 时不写入、不备份", TestAlreadyCorrectNoWrite);
            Run("重复键全部改为 3", TestDuplicateKeys);
            Run("相似键与注释行不受影响", TestSimilarKeysAndComments);
            Run("冒号后带空格时归一化", TestNormalizeSpacing);
            Run("保留 UTF-8 BOM / UTF-16 LE / UTF-16 BE / GBK 编码", TestEncodingPreservation);
            Run("畸形编码安全失败且不改文件", TestMalformedEncodingSafeFailure);
            Run("保留 LF 与 CRLF 换行风格", TestNewlinePreservation);
            Run("空文件也能补上键", TestEmptyFile);
            Run("中文与空格路径可用", TestChinesePath);
            Run("配置文件不存在时安全失败", TestMissingFile);
            Run("只读文件安全失败且不修改", TestReadOnlyFile);
            Run("备份文件名唯一", TestBackupNameUnique);
            Run("快捷方式解析到启动器目录", TestShortcutResolution);
            Run("标准位置优先于旧版位置", TestCanonicalPreferred);
            Run("只有旧版位置时给出说明", TestLegacyFallback);
            Run("没有配置文件时明确报错", TestNoConfigError);
            Run("直接拖入 Setup.ini 可用", TestDirectConfig);
            Run("拖入 PCL 文件夹可用", TestFolderResolution);
            Run("不支持的文件类型明确报错", TestUnsupportedFileType);
            Run("能识别正在运行的自身进程", TestRunningProcessDetection);
        }
        finally
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (Exception)
            {
                // 临时目录清理失败不影响测试结论。
            }
        }

        Console.WriteLine();
        Console.WriteLine("通过: " + _passed + "，失败: " + _failed);
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        _currentTest = name;
        int failedBefore = _failed;
        try
        {
            test();
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + " -> 未预期异常: " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (_failed == failedBefore) Console.WriteLine("PASS " + name);
        else Console.WriteLine("FAIL " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            _passed++;
            return;
        }
        _failed++;
        Console.WriteLine("  - " + _currentTest + ": " + message);
    }

    private static void CheckContains(string text, string expected, string message)
    {
        Check(text != null && text.IndexOf(expected, StringComparison.Ordinal) >= 0,
            message + "（期望包含：" + expected + "）");
    }

    private static SetupIniException ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (SetupIniException ex)
        {
            _passed++;
            return ex;
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("  - " + _currentTest + ": " + message + "（抛出了非预期异常 " + ex.GetType().Name + "）");
            return null;
        }

        _failed++;
        Console.WriteLine("  - " + _currentTest + ": " + message + "（没有抛出任何异常）");
        return null;
    }

    // ---------- 夹具 ----------

    private static string NewCaseDir(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteIni(string dir, byte[] bytes)
    {
        string path = Path.Combine(dir, "Setup.ini");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Utf8(string text)
    {
        return new UTF8Encoding(false).GetBytes(text);
    }

    private static byte[] Utf8Bom(string text)
    {
        return Concat(new byte[] { 0xEF, 0xBB, 0xBF }, new UTF8Encoding(false).GetBytes(text));
    }

    private static byte[] Utf16Le(string text)
    {
        return Concat(new byte[] { 0xFF, 0xFE }, new UnicodeEncoding(false, false).GetBytes(text));
    }

    private static byte[] Utf16Be(string text)
    {
        return Concat(new byte[] { 0xFE, 0xFF }, new UnicodeEncoding(true, false).GetBytes(text));
    }

    private static byte[] Gbk(string text)
    {
        return Encoding.GetEncoding(936).GetBytes(text);
    }

    private static byte[] Concat(byte[] left, byte[] right)
    {
        byte[] result = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, result, 0, left.Length);
        Buffer.BlockCopy(right, 0, result, left.Length, right.Length);
        return result;
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    private static string ReadUtf8(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int offset = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        return new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static bool HasTempLeftovers(string dir)
    {
        return Directory.GetFiles(dir, "*.tmp").Length > 0
            || Directory.GetFiles(dir, "*.pclblocker-*").Length > 0;
    }

    private static bool HasBackup(string dir)
    {
        return Directory.GetFiles(dir, "Setup.ini.bak-*").Length > 0;
    }

    // ---------- 测试 ----------

    private static void TestColonReplace()
    {
        string dir = NewCaseDir("colon");
        string original = "Version:1\r\n# 注释：SystemSystemUpdate:0\r\nSystemSystemUpdate:0\r\nSystemTheme:0\r\n";
        byte[] bytes = Utf8(original);
        string path = WriteIni(dir, bytes);

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Updated, "变更类型应为 Updated");
        Check(result.RewrittenKeyLines == 1, "只应匹配 1 行键（实际 " + result.RewrittenKeyLines + "）");
        Check(result.Verified, "应通过写入后校验");
        Check(result.BackupPath != null && File.Exists(result.BackupPath), "应生成备份文件");
        Check(result.BackupPath != null && BytesEqual(File.ReadAllBytes(result.BackupPath), bytes), "备份内容应等于原始字节");

        string text = ReadUtf8(path);
        CheckContains(text, "SystemSystemUpdate:3\r\n", "键应被改为 3");
        CheckContains(text, "# 注释：SystemSystemUpdate:0", "注释行不应被修改");
        CheckContains(text, "SystemTheme:0", "其它设置应保留");
        CheckContains(text, "Version:1", "其它设置应保留");
        Check(!HasTempLeftovers(dir), "不应残留临时文件");
    }

    private static void TestEqualTypo()
    {
        string dir = NewCaseDir("equal");
        string path = WriteIni(dir, Utf8("SystemSystemUpdate=1\r\nSystemTheme:0\r\n"));

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Updated, "变更类型应为 Updated");
        Check(result.NormalizedEqualLines == 1, "应记录 1 行等号写法");
        string text = ReadUtf8(path);
        CheckContains(text, "SystemSystemUpdate:3\r\n", "等号写法应改为冒号 3");
        Check(text.IndexOf("SystemSystemUpdate=", StringComparison.Ordinal) < 0, "不应残留等号写法");
        CheckContains(text, "SystemTheme:0", "其它设置应保留");
    }

    private static void TestInsertKey()
    {
        string dir = NewCaseDir("insert");
        string path = WriteIni(dir, Utf8("Version:1\r\nSystemTheme:0\r\n"));

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Inserted, "变更类型应为 Inserted");
        Check(result.Verified, "应通过写入后校验");
        Check(File.ReadAllText(path).EndsWith("SystemSystemUpdate:3\r\n", StringComparison.Ordinal),
            "应在末尾插入 SystemSystemUpdate:3 并保留 CRLF");
        CheckContains(ReadUtf8(path), "Version:1\r\nSystemTheme:0\r\n", "原有内容应完整保留");
    }

    private static void TestInsertWithoutTrailingNewline()
    {
        string dir = NewCaseDir("insert-no-eol");
        string path = WriteIni(dir, Utf8("Version:1"));

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Inserted, "变更类型应为 Inserted");
        string text = ReadUtf8(path);
        Check(text == "Version:1\r\nSystemSystemUpdate:3", "应插入一行且不新增末尾换行（实际：" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "）");
    }

    private static void TestAlreadyCorrectNoWrite()
    {
        string dir = NewCaseDir("noop");
        string path = WriteIni(dir, Utf8("SystemSystemUpdate:3\r\nOther:1\r\n"));
        DateTime stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        byte[] before = File.ReadAllBytes(path);

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.AlreadyCorrect, "变更类型应为 AlreadyCorrect");
        Check(result.BackupPath == null, "不应创建备份");
        Check(result.Verified, "应标记为已确认");
        Check(BytesEqual(before, File.ReadAllBytes(path)), "文件字节不应变化");
        Check(!HasBackup(dir), "目录里不应出现备份文件");
        Check(File.GetLastWriteTimeUtc(path) == stamp, "文件修改时间不应变化");
    }

    private static void TestDuplicateKeys()
    {
        string dir = NewCaseDir("duplicates");
        string path = WriteIni(dir, Utf8("SystemSystemUpdate:0\r\nSystemSystemUpdate:1\r\nSystemSystemUpdate=2\r\nSystemTheme:0\r\n"));

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.RewrittenKeyLines == 3, "应匹配 3 行（实际 " + result.RewrittenKeyLines + "）");
        Check(result.NormalizedEqualLines == 1, "应记录 1 行等号写法");
        string text = ReadUtf8(path);
        Check(CountOccurrences(text, "SystemSystemUpdate:3") == 3, "3 行都应变成 SystemSystemUpdate:3");
        Check(CountOccurrences(text, "SystemTheme:0") == 1, "其它设置应保留");
    }

    private static void TestSimilarKeysAndComments()
    {
        string dir = NewCaseDir("similar");
        string original = "SystemSystemUpdate2:0\r\nSystemSystemUpdateX:1\r\nSystemSystemUpdateNote:5\r\n"
            + "#SystemSystemUpdate:0\r\n;SystemSystemUpdate=0\r\n  #  SystemSystemUpdate:0\r\nSystemSystemUpdate:3\r\n";
        string path = WriteIni(dir, Utf8(original));
        byte[] before = File.ReadAllBytes(path);

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.AlreadyCorrect, "相似键不应触发修改");
        Check(result.RewrittenKeyLines == 1, "只应匹配真正的键（实际 " + result.RewrittenKeyLines + "）");
        Check(BytesEqual(before, File.ReadAllBytes(path)), "文件字节不应变化");
        Check(!HasBackup(dir), "不应创建备份");
    }

    private static void TestNormalizeSpacing()
    {
        string dir = NewCaseDir("spacing");
        string path = WriteIni(dir, Utf8("  SystemSystemUpdate: 3\r\n"));

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Updated, "带空格的写法应归一化");
        Check(ReadUtf8(path) == "  SystemSystemUpdate:3\r\n", "应保留缩进并去掉值前空格");
    }

    private static void TestEncodingPreservation()
    {
        // UTF-8 带 BOM
        string dir = NewCaseDir("enc-utf8bom");
        string path = WriteIni(dir, Utf8Bom("# 中文注释\r\nSystemSystemUpdate:0\r\n"));
        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);
        byte[] written = File.ReadAllBytes(path);
        Check(result.Change == SetupIniChange.Updated, "UTF-8 BOM 文件应被修改");
        Check(written.Length > 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF, "应保留 UTF-8 BOM");
        CheckContains(ReadUtf8(path), "# 中文注释", "中文注释应保留");
        CheckContains(ReadUtf8(path), "SystemSystemUpdate:3", "键应改为 3");

        // UTF-16 LE
        dir = NewCaseDir("enc-utf16le");
        path = WriteIni(dir, Utf16Le("# 中文注释\r\nSystemSystemUpdate:0\r\n"));
        result = SetupIniEditor.UpdateFile(path);
        written = File.ReadAllBytes(path);
        Check(result.Change == SetupIniChange.Updated, "UTF-16 LE 文件应被修改");
        Check(written[0] == 0xFF && written[1] == 0xFE, "应保留 UTF-16 LE BOM");
        string utf16Text = new UnicodeEncoding(false, false).GetString(written, 2, written.Length - 2);
        CheckContains(utf16Text, "# 中文注释", "UTF-16 LE 中文注释应保留");
        CheckContains(utf16Text, "SystemSystemUpdate:3", "UTF-16 LE 键应改为 3");

        // UTF-16 BE
        dir = NewCaseDir("enc-utf16be");
        path = WriteIni(dir, Utf16Be("# 中文注释\r\nSystemSystemUpdate:0\r\n"));
        result = SetupIniEditor.UpdateFile(path);
        written = File.ReadAllBytes(path);
        Check(result.Change == SetupIniChange.Updated, "UTF-16 BE 文件应被修改");
        Check(written[0] == 0xFE && written[1] == 0xFF, "应保留 UTF-16 BE BOM");
        string beText = new UnicodeEncoding(true, false).GetString(written, 2, written.Length - 2);
        CheckContains(beText, "SystemSystemUpdate:3", "UTF-16 BE 键应改为 3");

        // GBK
        dir = NewCaseDir("enc-gbk");
        path = WriteIni(dir, Gbk("# 中文注释\r\nSystemSystemUpdate:0\r\n"));
        result = SetupIniEditor.UpdateFile(path);
        written = File.ReadAllBytes(path);
        Check(result.Change == SetupIniChange.Updated, "GBK 文件应被修改");
        Check(!(written.Length > 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF), "GBK 文件不应被加 BOM");
        string gbkText = Encoding.GetEncoding(936).GetString(written);
        CheckContains(gbkText, "# 中文注释", "GBK 中文注释应保留");
        CheckContains(gbkText, "SystemSystemUpdate:3", "GBK 键应改为 3");
        Check(BytesEqual(written, Gbk(gbkText)), "GBK 内容应能按 GBK 原样往返");
    }

    private static void TestMalformedEncodingSafeFailure()
    {
        // BOM 声称是 UTF-8，但内容不是合法 UTF-8
        string dir = NewCaseDir("bad-utf8");
        byte[] bytes = Concat(new byte[] { 0xEF, 0xBB, 0xBF },
            Concat(Utf8("SystemSystemUpdate:0\r\n"), new byte[] { 0xFF }));
        string path = WriteIni(dir, bytes);
        SetupIniException ex = ExpectFailure(delegate { SetupIniEditor.UpdateFile(path); }, "非法 UTF-8 应报错");
        if (ex != null) CheckContains(ex.Message, "未做任何修改", "错误信息应说明未修改");
        Check(BytesEqual(bytes, File.ReadAllBytes(path)), "非法 UTF-8 文件不应被修改");
        Check(!HasBackup(dir), "非法 UTF-8 不应创建备份");
        Check(!HasTempLeftovers(dir), "非法 UTF-8 不应残留临时文件");

        // UTF-16 LE 字节数为奇数
        dir = NewCaseDir("bad-utf16-odd");
        byte[] whole = Utf16Le("SystemSystemUpdate:0\r\n");
        bytes = new byte[whole.Length - 1];
        Buffer.BlockCopy(whole, 0, bytes, 0, bytes.Length);
        path = WriteIni(dir, bytes);
        ex = ExpectFailure(delegate { SetupIniEditor.UpdateFile(path); }, "奇数长度 UTF-16 应报错");
        if (ex != null) CheckContains(ex.Message, "未做任何修改", "错误信息应说明未修改");
        Check(BytesEqual(bytes, File.ReadAllBytes(path)), "奇数长度 UTF-16 文件不应被修改");
        Check(!HasBackup(dir), "奇数长度 UTF-16 不应创建备份");

        // 无 BOM 的 UTF-16 LE（含 NUL 字节），不能当 UTF-8 追加一行
        dir = NewCaseDir("bad-utf16-nobom");
        bytes = new UnicodeEncoding(false, false).GetBytes("SystemSystemUpdate:0\r\n");
        path = WriteIni(dir, bytes);
        ex = ExpectFailure(delegate { SetupIniEditor.UpdateFile(path); }, "无 BOM 的 UTF-16 应报错");
        if (ex != null) CheckContains(ex.Message, "UTF-16", "错误信息应提到 UTF-16");
        Check(BytesEqual(bytes, File.ReadAllBytes(path)), "无 BOM UTF-16 文件不应被修改");
        Check(!HasBackup(dir), "无 BOM UTF-16 不应创建备份");

        // 非 GBK 的 ANSI 字节：末尾是一个没有尾字节的 GBK 前导字节
        dir = NewCaseDir("bad-ansi");
        bytes = Concat(Utf8("SystemSystemUpdate:0\r\n"), new byte[] { 0x81 });
        path = WriteIni(dir, bytes);
        ex = ExpectFailure(delegate { SetupIniEditor.UpdateFile(path); }, "非 GBK 的 ANSI 应报错");
        if (ex != null) CheckContains(ex.Message, "未做任何修改", "错误信息应说明未修改");
        Check(BytesEqual(bytes, File.ReadAllBytes(path)), "非 GBK 的 ANSI 文件不应被修改");
        Check(!HasBackup(dir), "非 GBK 的 ANSI 不应创建备份");
    }

    private static void TestNewlinePreservation()
    {
        string dir = NewCaseDir("eol-lf");
        string path = WriteIni(dir, Utf8("Version:1\nSystemSystemUpdate:0\n"));
        SetupIniEditor.UpdateFile(path);
        string lfText = ReadUtf8(path);
        Check(lfText.IndexOf('\r') < 0, "LF 文件不应被改成 CRLF");
        Check(lfText == "Version:1\nSystemSystemUpdate:3\n", "LF 文件应保持原样式换行");

        dir = NewCaseDir("eol-crlf");
        path = WriteIni(dir, Utf8("Version:1\r\nSystemSystemUpdate:0\r\n"));
        SetupIniEditor.UpdateFile(path);
        string crlfText = ReadUtf8(path);
        Check(crlfText == "Version:1\r\nSystemSystemUpdate:3\r\n", "CRLF 文件应保持 CRLF");

        dir = NewCaseDir("eol-insert-lf");
        path = WriteIni(dir, Utf8("Version:1\n"));
        SetupIniEditor.UpdateFile(path);
        Check(ReadUtf8(path) == "Version:1\nSystemSystemUpdate:3\n", "插入行应沿用 LF");
    }

    private static void TestEmptyFile()
    {
        string dir = NewCaseDir("empty");
        string path = WriteIni(dir, new byte[0]);

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Inserted, "空文件应插入键");
        Check(ReadUtf8(path) == "SystemSystemUpdate:3", "空文件应只写入键行");

        string bomDir = NewCaseDir("empty-bom");
        string bomPath = WriteIni(bomDir, new byte[] { 0xEF, 0xBB, 0xBF });
        SetupIniFileResult bomResult = SetupIniEditor.UpdateFile(bomPath);
        byte[] written = File.ReadAllBytes(bomPath);
        Check(bomResult.Change == SetupIniChange.Inserted, "只有 BOM 的文件应插入键");
        Check(written.Length > 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF, "应保留原有 BOM");
        Check(ReadUtf8(bomPath) == "SystemSystemUpdate:3", "BOM 之后应写入键行");
    }

    private static void TestChinesePath()
    {
        string dir = Path.Combine(_root, "中文 目录 测试");
        Directory.CreateDirectory(dir);
        string path = WriteIni(dir, Utf8("SystemSystemUpdate:0\r\n"));

        SetupIniFileResult result = SetupIniEditor.UpdateFile(path);

        Check(result.Change == SetupIniChange.Updated, "中文/空格路径应可写入");
        CheckContains(ReadUtf8(path), "SystemSystemUpdate:3", "键应改为 3");
        Check(result.BackupPath != null && File.Exists(result.BackupPath), "备份应创建在同一目录");
    }

    private static void TestMissingFile()
    {
        string dir = NewCaseDir("missing");
        string path = Path.Combine(dir, "Setup.ini");

        SetupIniException ex = ExpectFailure(delegate { SetupIniEditor.UpdateFile(path); }, "不存在的文件应报错");
        if (ex != null) CheckContains(ex.Message, "找不到配置文件", "错误信息应说明找不到配置文件");
        Check(!File.Exists(path), "不应创建文件");
    }

    private static void TestReadOnlyFile()
    {
        string dir = NewCaseDir("readonly");
        byte[] bytes = Utf8("SystemSystemUpdate:0\r\n");
        string path = WriteIni(dir, bytes);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            SetupIniException ex = ExpectFailure(delegate { SetupIniEditor.UpdateFile(path); }, "只读文件应报错");
            if (ex != null) CheckContains(ex.Message, "只读", "错误信息应提到只读");
            Check(BytesEqual(bytes, File.ReadAllBytes(path)), "只读文件内容不应变化");
            Check(!HasBackup(dir), "只读失败时不应留下备份");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    private static void TestBackupNameUnique()
    {
        string dir = NewCaseDir("backup-unique");
        string path = WriteIni(dir, Utf8("SystemSystemUpdate:0\r\n"));

        SetupIniFileResult first = SetupIniEditor.UpdateFile(path);
        File.WriteAllBytes(path, Utf8("SystemSystemUpdate:0\r\n"));
        SetupIniFileResult second = SetupIniEditor.UpdateFile(path);

        Check(first.BackupPath != null && second.BackupPath != null, "两次写入都应创建备份");
        Check(first.BackupPath != second.BackupPath, "备份文件名应唯一");
        Check(File.Exists(first.BackupPath) && File.Exists(second.BackupPath), "两个备份都应存在");
    }

    private static void TestShortcutResolution()
    {
        string dir = NewCaseDir("shortcut");
        string exePath = Path.Combine(dir, "PCL.exe");
        File.WriteAllBytes(exePath, new byte[0]);
        Directory.CreateDirectory(Path.Combine(dir, "PCL"));
        File.WriteAllBytes(Path.Combine(dir, "PCL", "Setup.ini"), Utf8("SystemSystemUpdate:0\r\n"));
        string lnkPath = Path.Combine(dir, "PCL 快捷方式.lnk");

        try
        {
            CreateShortcut(lnkPath, exePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  (跳过快捷方式用例：无法创建 .lnk，" + ex.Message + ")");
            return;
        }

        LauncherTarget target = LauncherResolver.Resolve(lnkPath);
        Check(target.SetupIniPath == Path.Combine(dir, "PCL", "Setup.ini"), "快捷方式应解析到 PCL\\Setup.ini");
        Check(target.LauncherExePath == exePath, "应解析出启动器 exe 路径");
        CheckContains(target.SelectionNote, "快捷方式", "定位说明应提到快捷方式");

        string txtPath = Path.Combine(dir, "note.txt");
        File.WriteAllText(txtPath, "x");
        string badLnk = Path.Combine(dir, "bad.lnk");
        CreateShortcut(badLnk, txtPath);
        SetupIniException ex2 = ExpectFailure(delegate { LauncherResolver.Resolve(badLnk); }, "指向 txt 的快捷方式应报错");
        if (ex2 != null) CheckContains(ex2.Message, "不是可执行文件", "错误信息应说明目标不是可执行文件");
    }

    private static void TestCanonicalPreferred()
    {
        string dir = NewCaseDir("canonical");
        string exePath = Path.Combine(dir, "PCL.exe");
        File.WriteAllBytes(exePath, new byte[0]);
        Directory.CreateDirectory(Path.Combine(dir, "PCL"));
        File.WriteAllBytes(Path.Combine(dir, "PCL", "Setup.ini"), Utf8("SystemSystemUpdate:0\r\n"));
        File.WriteAllBytes(Path.Combine(dir, "Setup.ini"), Utf8("SystemSystemUpdate:1\r\n"));

        LauncherTarget target = LauncherResolver.Resolve(exePath);

        Check(target.SetupIniPath == Path.Combine(dir, "PCL", "Setup.ini"), "应优先使用 PCL\\Setup.ini");
        CheckContains(target.SelectionNote, "标准位置", "定位说明应说明使用标准位置");
    }

    private static void TestLegacyFallback()
    {
        string dir = NewCaseDir("legacy");
        string exePath = Path.Combine(dir, "PCL.exe");
        File.WriteAllBytes(exePath, new byte[0]);
        File.WriteAllBytes(Path.Combine(dir, "Setup.ini"), Utf8("SystemSystemUpdate:0\r\n"));

        LauncherTarget target = LauncherResolver.Resolve(exePath);

        Check(target.SetupIniPath == Path.Combine(dir, "Setup.ini"), "只有旧版位置时应使用它");
        CheckContains(target.SelectionNote, "旧版", "定位说明应提示这是旧版位置");
    }

    private static void TestNoConfigError()
    {
        string dir = NewCaseDir("no-config");
        string exePath = Path.Combine(dir, "PCL.exe");
        File.WriteAllBytes(exePath, new byte[0]);

        SetupIniException ex = ExpectFailure(delegate { LauncherResolver.Resolve(exePath); }, "缺少配置应报错");
        if (ex != null)
        {
            CheckContains(ex.Message, "未找到 PCL 配置文件", "错误信息应说明未找到配置");
            CheckContains(ex.Message, "不会凭空创建", "错误信息应说明不会创建配置");
        }
        Check(!Directory.Exists(Path.Combine(dir, "PCL")), "不应创建 PCL 目录");
    }

    private static void TestDirectConfig()
    {
        string dir = NewCaseDir("direct");
        string path = WriteIni(dir, Utf8("SystemSystemUpdate:0\r\n"));

        LauncherTarget target = LauncherResolver.Resolve(path);

        Check(target.IsDirectConfig, "应识别为直接配置文件");
        Check(target.SetupIniPath == path, "应直接使用该文件");
    }

    private static void TestFolderResolution()
    {
        string dir = NewCaseDir("folder");
        Directory.CreateDirectory(Path.Combine(dir, "PCL"));
        File.WriteAllBytes(Path.Combine(dir, "PCL", "Setup.ini"), Utf8("SystemSystemUpdate:0\r\n"));

        LauncherTarget target = LauncherResolver.Resolve(dir);

        Check(target.SetupIniPath == Path.Combine(dir, "PCL", "Setup.ini"), "文件夹应解析到 PCL\\Setup.ini");
        CheckContains(target.SelectionNote, "文件夹", "定位说明应提到文件夹");
    }

    private static void TestUnsupportedFileType()
    {
        string dir = NewCaseDir("unsupported");
        string path = Path.Combine(dir, "readme.txt");
        File.WriteAllText(path, "x");

        SetupIniException ex = ExpectFailure(delegate { LauncherResolver.Resolve(path); }, "不支持的类型应报错");
        if (ex != null) CheckContains(ex.Message, "不支持的文件类型", "错误信息应说明类型不支持");
    }

    private static void TestRunningProcessDetection()
    {
        string self = Process.GetCurrentProcess().MainModule.FileName;
        string detected = LauncherResolver.FindRunningProcess(self);
        Check(detected != null, "应识别出正在运行的测试进程自身");
        Check(detected == null || detected.IndexOf("PID", StringComparison.Ordinal) > 0, "识别结果应包含 PID");

        string ghost = Path.Combine(_root, "ghost-" + Guid.NewGuid().ToString("N") + ".exe");
        Check(LauncherResolver.FindRunningProcess(ghost) == null, "不存在的进程应返回 null");
        Check(LauncherResolver.FindRunningProcess(null) == null, "空路径应返回 null");
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) throw new InvalidOperationException("WScript.Shell 不可用");

        object shell = Activator.CreateInstance(shellType);
        object shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { shortcutPath });
            shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut,
                new object[] { targetPath });
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}

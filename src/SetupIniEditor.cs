using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PclUpdateBlocker
{
    public enum SetupIniChange
    {
        AlreadyCorrect = 0,
        Updated = 1,
        Inserted = 2
    }

    public sealed class SetupIniException : Exception
    {
        public SetupIniException(string message) : base(message) { }

        public SetupIniException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public sealed class SetupIniTextEdit
    {
        public string NewText;
        public SetupIniChange Change;
        public int RewrittenKeyLines;
        public int NormalizedEqualLines;
    }

    public sealed class SetupIniFileResult
    {
        public string FilePath;
        public string EncodingName;
        public SetupIniChange Change;
        public int RewrittenKeyLines;
        public int NormalizedEqualLines;
        public string BackupPath;
        public bool Verified;
    }

    /// <summary>
    /// PCL 的 Setup.ini 使用 KEY:VALUE 而不是标准 INI 的 KEY=VALUE，
    /// 因此这里按行解析，保留缩进、换行风格和文件编码。
    /// </summary>
    public sealed class SetupIniEncoding
    {
        private readonly string _name;
        private readonly Encoding _body;
        private readonly byte[] _preamble;

        public SetupIniEncoding(string name, Encoding body, byte[] preamble)
        {
            _name = name;
            _body = body;
            _preamble = preamble;
        }

        public string Name
        {
            get { return _name; }
        }

        public string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= _preamble.Length) return string.Empty;
            return _body.GetString(bytes, _preamble.Length, bytes.Length - _preamble.Length);
        }

        public byte[] Encode(string text)
        {
            byte[] body = _body.GetBytes(text ?? string.Empty);
            if (_preamble.Length == 0) return body;
            byte[] result = new byte[_preamble.Length + body.Length];
            Buffer.BlockCopy(_preamble, 0, result, 0, _preamble.Length);
            Buffer.BlockCopy(body, 0, result, _preamble.Length, body.Length);
            return result;
        }
    }

    public static class SetupIniEditor
    {
        public const string KeyName = "SystemSystemUpdate";
        public const string DesiredValue = "3";
        public const string CanonicalLine = KeyName + ":" + DesiredValue;

        private sealed class TextLine
        {
            public string Content;
            public string EndOfLine;
        }

        public static SetupIniEncoding DetectEncoding(byte[] bytes)
        {
            if (bytes == null) bytes = new byte[0];

            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                throw new SetupIniException("配置文件是 UTF-32 (LE) 编码，本工具不支持，请先另存为 UTF-8 或 UTF-16。未做任何修改。");

            if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                throw new SetupIniException("配置文件是 UTF-32 (BE) 编码，本工具不支持，请先另存为 UTF-8 或 UTF-16。未做任何修改。");

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new SetupIniEncoding("UTF-16 LE (带 BOM)", new UnicodeEncoding(false, false, true), new byte[] { 0xFF, 0xFE });

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new SetupIniEncoding("UTF-16 BE (带 BOM)", new UnicodeEncoding(true, false, true), new byte[] { 0xFE, 0xFF });

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new SetupIniEncoding("UTF-8 (带 BOM)", new UTF8Encoding(false, true), new byte[] { 0xEF, 0xBB, 0xBF });

            // 没有 BOM 却含 NUL 字节：几乎可以确定是无 BOM 的 UTF-16/UTF-32。
            // 若当成 UTF-8 处理再追加一行，会写出混合编码的文件，所以直接拒绝。
            if (ContainsNul(bytes))
                throw new SetupIniException("配置文件里含有 NUL 字节，看起来是没有 BOM 的 UTF-16/UTF-32 编码。"
                    + "本工具只支持 UTF-8（带或不带 BOM）、带 BOM 的 UTF-16 和 ANSI/GBK，"
                    + "请用 UTF-8 另存后再试。未做任何修改。");

            if (IsValidUtf8(bytes))
                return new SetupIniEncoding("UTF-8 (无 BOM)", new UTF8Encoding(false, true), new byte[0]);

            return new SetupIniEncoding("ANSI/GBK", GetAnsiEncoding(), new byte[0]);
        }

        public static SetupIniTextEdit BuildEdit(string text)
        {
            if (text == null) text = string.Empty;

            List<TextLine> lines = SplitLines(text);
            string endOfLine = DetectEndOfLine(lines);

            int rewritten = 0;
            int normalized = 0;
            bool changed = false;

            foreach (TextLine line in lines)
            {
                string indent;
                char separator;
                if (!TryMatchKeyLine(line.Content, out indent, out separator)) continue;

                rewritten++;
                if (separator == '=') normalized++;

                string replacement = indent + CanonicalLine;
                if (!string.Equals(replacement, line.Content, StringComparison.Ordinal))
                {
                    line.Content = replacement;
                    changed = true;
                }
            }

            SetupIniTextEdit edit = new SetupIniTextEdit();
            edit.RewrittenKeyLines = rewritten;
            edit.NormalizedEqualLines = normalized;

            if (rewritten == 0)
            {
                edit.Change = SetupIniChange.Inserted;
                edit.NewText = AppendKeyLine(text, endOfLine);
            }
            else if (changed)
            {
                edit.Change = SetupIniChange.Updated;
                edit.NewText = Rebuild(lines);
            }
            else
            {
                edit.Change = SetupIniChange.AlreadyCorrect;
                edit.NewText = text;
            }

            return edit;
        }

        public static SetupIniFileResult UpdateFile(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
                throw new SetupIniException("没有指定配置文件路径。");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch (Exception ex)
            {
                throw new SetupIniException("配置文件路径无效：" + path, ex);
            }

            if (!File.Exists(fullPath))
                throw new SetupIniException("找不到配置文件：" + fullPath);

            byte[] original;
            try
            {
                original = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex)
            {
                throw new SetupIniException("无法读取配置文件：" + fullPath + "（" + ex.Message + "）", ex);
            }

            SetupIniEncoding encoding = DetectEncoding(original);
            string text = DecodeLossless(encoding, original, fullPath);
            SetupIniTextEdit edit = BuildEdit(text);

            SetupIniFileResult result = new SetupIniFileResult();
            result.FilePath = fullPath;
            result.EncodingName = encoding.Name;
            result.Change = edit.Change;
            result.RewrittenKeyLines = edit.RewrittenKeyLines;
            result.NormalizedEqualLines = edit.NormalizedEqualLines;

            if (edit.Change == SetupIniChange.AlreadyCorrect)
            {
                result.Verified = true;
                return result;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(fullPath);
            }
            catch (Exception ex)
            {
                throw new SetupIniException("无法读取配置文件属性：" + fullPath + "（" + ex.Message + "）", ex);
            }

            if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                throw new SetupIniException("配置文件是只读的，请手动取消只读属性后重试（本工具不会修改文件属性）：" + fullPath);

            byte[] updated;
            try
            {
                updated = encoding.Encode(edit.NewText);
            }
            catch (Exception ex)
            {
                throw new SetupIniException("无法按原编码（" + encoding.Name + "）写回配置文件：" + ex.Message
                    + "\n未做任何修改。", ex);
            }

            string directory = Path.GetDirectoryName(fullPath);
            string tempPath = Path.Combine(directory,
                Path.GetFileName(fullPath) + ".pclblocker-" + Guid.NewGuid().ToString("N") + ".tmp");
            string backupPath = BuildBackupPath(fullPath);

            try
            {
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(updated, 0, updated.Length);
                    stream.Flush(true);
                }

                ReplaceWithBackup(tempPath, fullPath, backupPath);
                result.BackupPath = backupPath;

                try
                {
                    File.SetAttributes(fullPath, attributes);
                }
                catch (Exception)
                {
                    // 属性还原失败不影响内容，忽略。
                }

                try
                {
                    VerifyWritten(fullPath, backupPath, original, updated, encoding);
                }
                catch (Exception ex)
                {
                    bool restored = TryRestoreBackup(fullPath, backupPath);
                    string reason = (ex is SetupIniException)
                        ? ex.Message
                        : ("写入后无法重新读取文件：" + ex.Message);
                    string restoreNote = restored
                        ? "已用备份覆盖回原内容，请打开文件确认。"
                        : "自动还原未成功，请手动用备份还原。";
                    throw new SetupIniException(reason + "\n" + restoreNote + "\n备份：" + backupPath, ex);
                }

                result.Verified = true;
                return result;
            }
            catch (SetupIniException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SetupIniException("写入配置文件失败：" + ex.Message
                    + (File.Exists(backupPath)
                        ? "\n请检查文件内容是否完整，原始内容已保存到备份：" + backupPath
                        : "\n请检查文件内容是否完整。"), ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // 临时文件清理失败不影响结果。
                }
            }
        }

        public static string DescribeChange(SetupIniChange change)
        {
            if (change == SetupIniChange.AlreadyCorrect) return "本来就是 " + CanonicalLine + "，未做任何修改";
            if (change == SetupIniChange.Inserted) return "配置中原来没有该键，已在文件末尾新增 " + CanonicalLine;
            return "已把 " + KeyName + " 改写为 3";
        }

        private static void ReplaceWithBackup(string tempPath, string targetPath, string backupPath)
        {
            try
            {
                File.Replace(tempPath, targetPath, backupPath, true);
                return;
            }
            catch (Exception)
            {
                // 部分文件系统（如某些 FAT/exFAT 卷）不支持 ReplaceFile，退回到“先移走原文件再放入新文件”。
                if (!File.Exists(targetPath) || File.Exists(backupPath)) throw;
            }

            File.Move(targetPath, backupPath);
            try
            {
                File.Move(tempPath, targetPath);
            }
            catch (Exception)
            {
                if (!File.Exists(targetPath) && File.Exists(backupPath)) File.Move(backupPath, targetPath);
                throw;
            }
        }

        private static void VerifyWritten(string path, string backupPath, byte[] original, byte[] expected,
            SetupIniEncoding encoding)
        {
            byte[] written = File.ReadAllBytes(path);
            if (!BytesEqual(written, expected))
                throw new SetupIniException("写入后校验失败：文件内容与预期写入的字节不一致。");

            byte[] backupBytes = File.ReadAllBytes(backupPath);
            if (!BytesEqual(backupBytes, original))
                throw new SetupIniException("写入后校验失败：备份内容与原文件不一致。");

            SetupIniTextEdit check = BuildEdit(encoding.Decode(written));
            if (check.Change != SetupIniChange.AlreadyCorrect)
                throw new SetupIniException("写入后校验失败：重新读取的配置里 " + KeyName + " 不是 3。");
        }

        private static bool TryRestoreBackup(string path, string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath)) return false;
                File.Copy(backupPath, path, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string BuildBackupPath(string fullPath)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string candidate = fullPath + ".bak-" + stamp;
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = fullPath + ".bak-" + stamp + "-" + index;
                index++;
            }
            return candidate;
        }

        /// <summary>
        /// 解码必须无损：解码失败、或“解码再编码”后字节不一致，都直接报错，
        /// 以免把无法识别的字节替换成占位符后覆盖掉无关设置。
        /// </summary>
        private static string DecodeLossless(SetupIniEncoding encoding, byte[] bytes, string fullPath)
        {
            string text;
            try
            {
                text = encoding.Decode(bytes);
            }
            catch (Exception ex)
            {
                throw new SetupIniException("无法按 " + encoding.Name + " 无损解码配置文件：" + fullPath
                    + "\n为避免损坏其它设置，未做任何修改。请用 UTF-8 或带 BOM 的 UTF-16 重新保存后再试。", ex);
            }

            byte[] roundtrip;
            try
            {
                roundtrip = encoding.Encode(text);
            }
            catch (Exception ex)
            {
                throw new SetupIniException("无法按 " + encoding.Name + " 无损重新编码配置文件：" + fullPath
                    + "\n为避免损坏其它设置，未做任何修改。请用 UTF-8 或带 BOM 的 UTF-16 重新保存后再试。", ex);
            }

            if (!BytesEqual(roundtrip, bytes))
                throw new SetupIniException("配置文件的 " + encoding.Name + " 编码无法无损往返（解码再编码后字节不一致）：" + fullPath
                    + "\n为避免损坏其它设置，未做任何修改。请用 UTF-8 或带 BOM 的 UTF-16 重新保存后再试。");

            return text;
        }

        private static bool ContainsNul(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0x00) return true;
            }
            return false;
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Encoding GetAnsiEncoding()
        {
            try
            {
                return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch (Exception)
            {
                return Encoding.Default;
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null) return left == right;
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }
            return true;
        }

        private static List<TextLine> SplitLines(string text)
        {
            List<TextLine> lines = new List<TextLine>();
            int start = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\r')
                {
                    string endOfLine = (i + 1 < text.Length && text[i + 1] == '\n') ? "\r\n" : "\r";
                    lines.Add(new TextLine { Content = text.Substring(start, i - start), EndOfLine = endOfLine });
                    i += endOfLine.Length;
                    start = i;
                }
                else if (c == '\n')
                {
                    lines.Add(new TextLine { Content = text.Substring(start, i - start), EndOfLine = "\n" });
                    i++;
                    start = i;
                }
                else
                {
                    i++;
                }
            }

            if (start < text.Length)
                lines.Add(new TextLine { Content = text.Substring(start), EndOfLine = string.Empty });

            return lines;
        }

        private static string DetectEndOfLine(List<TextLine> lines)
        {
            foreach (TextLine line in lines)
            {
                if (!string.IsNullOrEmpty(line.EndOfLine)) return line.EndOfLine;
            }
            return "\r\n";
        }

        private static string Rebuild(List<TextLine> lines)
        {
            StringBuilder builder = new StringBuilder();
            foreach (TextLine line in lines)
            {
                builder.Append(line.Content);
                builder.Append(line.EndOfLine);
            }
            return builder.ToString();
        }

        private static string AppendKeyLine(string text, string endOfLine)
        {
            if (text.Length == 0) return CanonicalLine;

            if (text.EndsWith("\r\n", StringComparison.Ordinal)
                || text.EndsWith("\n", StringComparison.Ordinal)
                || text.EndsWith("\r", StringComparison.Ordinal))
            {
                return text + CanonicalLine + endOfLine;
            }

            return text + endOfLine + CanonicalLine;
        }

        /// <summary>
        /// 只匹配“生效”的键行：键名必须与 SystemSystemUpdate 完全一致（PCL 区分大小写），
        /// 以 # 或 ; 开头的注释行、以及 SystemSystemUpdate2 这类相似键都不处理。
        /// </summary>
        private static bool TryMatchKeyLine(string content, out string indent, out char separator)
        {
            indent = null;
            separator = ':';

            if (content == null) return false;

            string trimmedStart = content.TrimStart(' ', '\t');
            if (trimmedStart.Length == 0) return false;

            char first = trimmedStart[0];
            if (first == '#' || first == ';') return false;

            int separatorIndex = -1;
            char separatorChar = '\0';
            for (int i = 0; i < trimmedStart.Length; i++)
            {
                char c = trimmedStart[i];
                if (c == ':' || c == '=')
                {
                    separatorIndex = i;
                    separatorChar = c;
                    break;
                }
            }

            if (separatorIndex < 0) return false;

            string key = trimmedStart.Substring(0, separatorIndex).Trim();
            if (!string.Equals(key, KeyName, StringComparison.Ordinal)) return false;

            indent = content.Substring(0, content.Length - trimmedStart.Length);
            separator = separatorChar;
            return true;
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using System.Windows.Forms;

namespace PclUpdateBlocker
{
    /// <summary>
    /// 主窗口：只负责拖放 / 选择文件与结果展示，所有修改逻辑都在 SetupIniEditor 里。
    /// 布局按 96 DPI 设计，运行时按实际 DPI 缩放，因此不启用 WinForms 自动缩放。
    /// </summary>
    public sealed class MainForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(47, 111, 208);
        private static readonly Color AccentHover = Color.FromArgb(36, 90, 173);
        private static readonly Color AccentSoft = Color.FromArgb(235, 242, 252);
        private static readonly Color DropBackground = Color.FromArgb(243, 248, 254);
        private static readonly Color DropBackgroundActive = Color.FromArgb(226, 238, 253);
        private static readonly Color DropBorder = Color.FromArgb(163, 192, 230);
        private static readonly Color WindowBackground = Color.FromArgb(251, 252, 253);
        private static readonly Color SurfaceBorder = Color.FromArgb(214, 221, 230);
        private static readonly Color TextPrimary = Color.FromArgb(32, 40, 52);
        private static readonly Color TextSecondary = Color.FromArgb(92, 104, 120);
        private static readonly Color SuccessText = Color.FromArgb(22, 111, 69);
        private static readonly Color FailureText = Color.FromArgb(178, 38, 30);

        private readonly float _scale;
        private readonly TableLayoutPanel _layout;
        private readonly BorderPanel _dropPanel;
        private readonly Label _dropTitle;
        private readonly Label _dropHint;
        private readonly Label _statusLabel;
        private readonly LinkLabel _detailsLink;
        private readonly TextBox _detailsBox;
        private readonly Button _pickButton;
        private readonly Button _closeButton;
        private bool _detailsExpanded;
        private bool _dragHighlight;

        public MainForm(string initialPath)
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
            {
                _scale = screen.DpiX / 96F;
            }

            Text = "PCL 更新设置工具";
            AutoScaleMode = AutoScaleMode.None;
            BackColor = WindowBackground;
            Font = new Font(PickUiFontFamily(), 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(S(680), S(600));
            MinimumSize = new Size(S(620), S(540));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            DoubleBuffered = true;
            KeyDown += OnFormKeyDown;

            _layout = new TableLayoutPanel();
            _layout.Dock = DockStyle.Fill;
            _layout.BackColor = WindowBackground;
            _layout.Padding = new Padding(S(22), S(18), S(22), S(18));
            _layout.ColumnCount = 1;
            _layout.RowCount = 7;
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(_layout);

            // 标题
            FlowLayoutPanel header = new FlowLayoutPanel();
            header.FlowDirection = FlowDirection.TopDown;
            header.WrapContents = false;
            header.AutoSize = true;
            header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0, 0, 0, S(12));
            _layout.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.AutoSize = true;
            title.Text = "PCL 更新设置工具";
            title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            title.ForeColor = TextPrimary;
            title.Margin = new Padding(0, 0, 0, S(3));
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Text = "把 PCL 的本地更新偏好 SystemSystemUpdate 设为 3";
            subtitle.ForeColor = TextSecondary;
            subtitle.Margin = new Padding(0);
            header.Controls.Add(subtitle);

            // 退出 PCL 提示
            Label notice = new Label();
            notice.AutoSize = true;
            notice.Dock = DockStyle.Fill;
            notice.BackColor = AccentSoft;
            notice.ForeColor = TextSecondary;
            notice.Padding = new Padding(S(12), S(7), S(12), S(7));
            notice.Margin = new Padding(0, 0, 0, S(14));
            notice.Text = "使用前请完全退出 PCL：本工具不会结束进程，也不会启动 PCL。";
            _layout.Controls.Add(notice, 0, 1);

            // 拖放区（同时是选择文件的入口）
            _dropPanel = new BorderPanel();
            _dropPanel.Dock = DockStyle.Fill;
            _dropPanel.BackColor = DropBackground;
            _dropPanel.MinimumSize = new Size(0, S(132));
            _dropPanel.Margin = new Padding(0, 0, 0, S(14));
            _dropPanel.Cursor = Cursors.Hand;
            _dropPanel.AccessibleName = "拖放区域";
            _dropPanel.AccessibleDescription = "把 PCL.exe、它的快捷方式、Setup.ini 或 PCL 文件夹拖到这里，也可以点击选择文件";
            _dropPanel.Paint += DrawDropBorder;
            _dropPanel.Resize += delegate { LayoutDropContent(); };
            _layout.Controls.Add(_dropPanel, 0, 2);

            // 拖放区里的两行文字只占自身大小，虚线边框才能完整显示。
            _dropTitle = new Label();
            _dropTitle.AutoSize = true;
            _dropTitle.BackColor = Color.Transparent;
            _dropTitle.Font = new Font(Font.FontFamily, 11.5F, FontStyle.Bold);
            _dropTitle.ForeColor = TextPrimary;
            _dropTitle.Text = "把 PCL.exe 或它的快捷方式拖到这里";
            _dropPanel.Controls.Add(_dropTitle);

            _dropHint = new Label();
            _dropHint.AutoSize = true;
            _dropHint.BackColor = Color.Transparent;
            _dropHint.ForeColor = TextSecondary;
            _dropHint.Text = "也支持 Setup.ini 或 PCL 文件夹 · 点击此处选择文件";
            _dropPanel.Controls.Add(_dropHint);

            LayoutDropContent();

            // 结果摘要
            _statusLabel = new Label();
            _statusLabel.AutoSize = true;
            _statusLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            _statusLabel.ForeColor = TextSecondary;
            _statusLabel.Margin = new Padding(0, 0, 0, S(5));
            _statusLabel.Text = "等待拖入 PCL 启动器…";
            _statusLabel.AccessibleName = "处理结果";
            _layout.Controls.Add(_statusLabel, 0, 3);

            // 详细信息开关
            _detailsLink = new LinkLabel();
            _detailsLink.AutoSize = true;
            _detailsLink.Text = "显示详细信息（路径、备份）";
            _detailsLink.LinkColor = Accent;
            _detailsLink.ActiveLinkColor = AccentHover;
            _detailsLink.VisitedLinkColor = Accent;
            _detailsLink.LinkBehavior = LinkBehavior.HoverUnderline;
            _detailsLink.Margin = new Padding(0, 0, 0, S(4));
            _detailsLink.LinkClicked += OnDetailsLinkClicked;
            _layout.Controls.Add(_detailsLink, 0, 4);

            _detailsBox = new TextBox();
            _detailsBox.Dock = DockStyle.Fill;
            _detailsBox.Multiline = true;
            _detailsBox.ReadOnly = true;
            _detailsBox.WordWrap = true;
            _detailsBox.ScrollBars = ScrollBars.Vertical;
            _detailsBox.BorderStyle = BorderStyle.FixedSingle;
            _detailsBox.BackColor = Color.White;
            _detailsBox.ForeColor = TextPrimary;
            _detailsBox.Visible = false;
            _detailsBox.Margin = new Padding(0, 0, 0, S(12));
            _detailsBox.AccessibleName = "详细信息";
            _detailsBox.Text = HelpText();
            _layout.Controls.Add(_detailsBox, 0, 5);

            // 按钮
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.Margin = new Padding(0);
            _layout.Controls.Add(buttons, 0, 6);

            _closeButton = CreateSecondaryButton("退出(&X)");
            _closeButton.AccessibleDescription = "关闭本工具，不做任何修改";
            _closeButton.TabIndex = 1;
            _closeButton.Click += OnCloseClick;
            buttons.Controls.Add(_closeButton);

            _pickButton = CreatePrimaryButton("选择 PCL 启动器(&O)…");
            _pickButton.AccessibleDescription = "浏览并选择 PCL.exe 或它的快捷方式";
            _pickButton.TabIndex = 0;
            _pickButton.Click += OnPickClick;
            buttons.Controls.Add(_pickButton);

            // 键盘顺序：主按钮 → 退出 → 详细信息
            buttons.TabIndex = 0;
            _detailsLink.TabIndex = 1;
            _detailsBox.TabIndex = 2;

            _layout.Resize += delegate { UpdateStatusWrapWidth(); };
            UpdateStatusWrapWidth();

            AttachDropTarget(this);
            AttachDropTarget(_dropPanel);
            AttachDropTarget(_dropTitle);
            AttachDropTarget(_dropHint);

            _dropPanel.Click += OnPickClick;
            _dropTitle.Click += OnPickClick;
            _dropHint.Click += OnPickClick;

            if (!string.IsNullOrEmpty(initialPath))
            {
                // 拖到 exe 图标上启动时，参数就是被拖入的文件。
                Shown += delegate { HandlePath(initialPath); };
            }
        }

        private int S(int designPixels)
        {
            return (int)Math.Round(designPixels * _scale, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 优先使用中文界面字体；Windows 7 没有 Microsoft YaHei UI 时退回 Microsoft YaHei。
        /// </summary>
        private static string PickUiFontFamily()
        {
            string[] preferred = { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
            try
            {
                using (InstalledFontCollection installed = new InstalledFontCollection())
                {
                    FontFamily[] families = installed.Families;
                    foreach (string name in preferred)
                    {
                        foreach (FontFamily family in families)
                        {
                            if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 字体枚举失败时退回系统默认字体。
            }

            return SystemFonts.MessageBoxFont.FontFamily.Name;
        }

        private static string HelpText()
        {
            return "使用步骤：\r\n"
                + "1. 完全退出 PCL（本工具只检查是否在运行，不会替你结束它）。\r\n"
                + "2. 把 PCL.exe 或它的快捷方式拖进窗口，也可以点击「选择 PCL 启动器」。\r\n"
                + "3. 工具会定位 <启动器目录>\\PCL\\Setup.ini，把 SystemSystemUpdate 设为 3，写入前自动备份。\r\n"
                + "\r\n"
                + "也可以直接拖入 Setup.ini 或 PCL 所在文件夹。\r\n"
                + "本工具只修改本地更新偏好，不恢复已被移除的功能，也不保证每个 PCL 版本都遵守该设置。";
        }

        private Button CreatePrimaryButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(S(96), S(32));
            button.Padding = new Padding(S(12), S(5), S(12), S(5));
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentHover;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(S(8), 0, 0, 0);
            return button;
        }

        private Button CreateSecondaryButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(S(76), S(32));
            button.Padding = new Padding(S(12), S(5), S(12), S(5));
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = SurfaceBorder;
            button.FlatAppearance.MouseOverBackColor = AccentSoft;
            button.FlatAppearance.MouseDownBackColor = AccentSoft;
            button.BackColor = Color.White;
            button.ForeColor = TextPrimary;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(S(8), 0, 0, 0);
            return button;
        }

        private void DrawDropBorder(object sender, PaintEventArgs e)
        {
            BorderPanel panel = sender as BorderPanel;
            if (panel == null) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float width = _dragHighlight ? 2F * _scale : 1.5F * _scale;
            float inset = width / 2F + S(1);
            RectangleF bounds = new RectangleF(inset, inset,
                Math.Max(1F, panel.Width - inset * 2F), Math.Max(1F, panel.Height - inset * 2F));

            using (GraphicsPath path = RoundedRectangle(bounds, S(10)))
            using (Pen pen = new Pen(_dragHighlight ? Accent : DropBorder, width))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new float[] { 4F, 3F };
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(1F, radius * 2F);
            if (diameter > bounds.Width) diameter = bounds.Width;
            if (diameter > bounds.Height) diameter = bounds.Height;

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
            path.CloseFigure();
            return path;
        }

        private void AttachDropTarget(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += OnDragEnter;
            control.DragOver += OnDragEnter;
            control.DragLeave += OnDragLeave;
            control.DragDrop += OnDragDrop;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            bool acceptsFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
            SetDragHighlight(acceptsFiles);
        }

        private void OnDragLeave(object sender, EventArgs e)
        {
            SetDragHighlight(false);
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            SetDragHighlight(false);
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            HandlePath(files[0]);
        }

        private void SetDragHighlight(bool highlight)
        {
            if (_dragHighlight == highlight) return;
            _dragHighlight = highlight;
            _dropPanel.BackColor = highlight ? DropBackgroundActive : DropBackground;
            _dropPanel.Invalidate();
        }

        /// <summary>
        /// 拖放区里的两行文字手动居中：用布局容器会把整块背景盖住，虚线边框就看不见了。
        /// </summary>
        private void LayoutDropContent()
        {
            if (_dropTitle == null || _dropHint == null) return;

            int gap = S(7);
            int totalHeight = _dropTitle.Height + gap + _dropHint.Height;
            int top = Math.Max(S(10), (_dropPanel.Height - totalHeight) / 2);

            _dropTitle.Location = new Point(Math.Max(S(10), (_dropPanel.Width - _dropTitle.Width) / 2), top);
            _dropHint.Location = new Point(Math.Max(S(10), (_dropPanel.Width - _dropHint.Width) / 2),
                top + _dropTitle.Height + gap);
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !e.Handled)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnDetailsLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            SetDetailsExpanded(!_detailsExpanded);
        }

        private void SetDetailsExpanded(bool expanded)
        {
            if (_detailsExpanded == expanded) return;
            _detailsExpanded = expanded;
            _detailsBox.Visible = expanded;
            _layout.RowStyles[2] = new RowStyle(SizeType.Percent, expanded ? 44F : 100F);
            _layout.RowStyles[5] = expanded
                ? new RowStyle(SizeType.Percent, 56F)
                : new RowStyle(SizeType.Absolute, 0F);
            _detailsLink.Text = expanded ? "收起详细信息" : "显示详细信息（路径、备份）";
        }

        private void OnPickClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 PCL 启动器";
                dialog.Filter = "PCL 启动器或快捷方式 (*.exe;*.lnk)|*.exe;*.lnk|PCL 配置文件 (Setup.ini)|Setup.ini|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) HandlePath(dialog.FileName);
            }
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            Close();
        }

        private void SetStatus(string text, Color color)
        {
            _statusLabel.ForeColor = color;
            _statusLabel.Text = text;
        }

        /// <summary>
        /// 状态行只占一行，但可以随窗口宽度换行，避免窄窗口下被截断。
        /// </summary>
        private void UpdateStatusWrapWidth()
        {
            int available = _layout.ClientSize.Width - _layout.Padding.Horizontal;
            _statusLabel.MaximumSize = new Size(Math.Max(S(200), available), 0);
        }

        /// <summary>
        /// 多行 TextBox 只认 CRLF：把上游消息里单独的 \n 统一成 CRLF，保留原来的分行结构。
        /// </summary>
        private static string NormalizeNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        private void SetDetails(string text)
        {
            _detailsBox.Text = NormalizeNewlines(text);
        }

        private void HandlePath(string path)
        {
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            SetStatus("正在处理…", TextSecondary);
            SetDetails("正在定位配置文件：\r\n" + path);
            _statusLabel.Refresh();
            _detailsBox.Refresh();
            try
            {
                LauncherTarget target = LauncherResolver.Resolve(path);

                string running = LauncherResolver.FindRunningProcess(target.LauncherExePath);
                if (running != null)
                {
                    ShowFailure("检测到 PCL 正在运行：" + running + "\r\n请完全退出 PCL 后再试（本工具不会结束进程）。");
                    return;
                }

                SetupIniFileResult result = SetupIniEditor.UpdateFile(target.SetupIniPath);
                ShowSuccess(target, result);
            }
            catch (SetupIniException ex)
            {
                ShowFailure(ex.Message);
            }
            catch (Exception ex)
            {
                ShowFailure("发生未预期的错误，未确认修改结果：\r\n" + ex.Message);
            }
            finally
            {
                Cursor = previous;
            }
        }

        private void ShowSuccess(LauncherTarget target, SetupIniFileResult result)
        {
            if (result.Change == SetupIniChange.AlreadyCorrect)
                SetStatus("无需修改：SystemSystemUpdate 已经是 3", SuccessText);
            else if (result.Change == SetupIniChange.Inserted)
                SetStatus("完成：已新增 SystemSystemUpdate:3", SuccessText);
            else
                SetStatus("完成：SystemSystemUpdate 已设为 3", SuccessText);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("配置文件：" + result.FilePath);
            builder.AppendLine("文件编码：" + result.EncodingName);
            builder.AppendLine("定位方式：" + target.SelectionNote);
            builder.AppendLine("结果：" + SetupIniEditor.DescribeChange(result.Change));

            if (result.RewrittenKeyLines > 1)
                builder.AppendLine("共处理 " + result.RewrittenKeyLines + " 行同名键（重复键全部改为 3）。");
            if (result.NormalizedEqualLines > 0)
                builder.AppendLine("其中 " + result.NormalizedEqualLines + " 行原本是 SystemSystemUpdate=... 的等号写法，已改为 PCL 使用的冒号写法。");

            if (!string.IsNullOrEmpty(result.BackupPath))
                builder.AppendLine("备份：" + result.BackupPath);
            else
                builder.AppendLine("备份：未创建（文件本来就是目标状态，未写入）");

            builder.AppendLine("校验：" + (result.Verified ? "已重新读取并核对文件内容" : "未执行"));
            builder.AppendLine();
            builder.AppendLine("接下来：");
            builder.AppendLine("· 重新启动 PCL 后，新配置才会被读取。");
            builder.AppendLine("· 如需还原，把备份文件复制回 Setup.ini 即可。");
            builder.AppendLine("· 本工具只修改本地更新偏好，不保证每个 PCL 版本都遵守该设置。");

            _detailsBox.Text = NormalizeNewlines(builder.ToString());
            SetDetailsExpanded(true);
        }

        private void ShowFailure(string message)
        {
            SetStatus("未完成：" + FirstLine(message), FailureText);
            SetDetails(message + "\r\n\r\n" + HelpText());
            SetDetailsExpanded(true);
        }

        private static string FirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return "请查看详细信息";

            string first = message;
            int index = first.IndexOfAny(new char[] { '\r', '\n' });
            if (index >= 0) first = first.Substring(0, index);
            first = first.Trim();
            if (first.Length > 46) first = first.Substring(0, 46) + "…";
            return first.Length == 0 ? "请查看详细信息" : first;
        }

        /// <summary>
        /// 拖放区需要自己重绘虚线边框，因此打开重绘与双缓冲。
        /// </summary>
        private sealed class BorderPanel : Panel
        {
            public BorderPanel()
            {
                SetStyle(ControlStyles.ResizeRedraw
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint, true);
            }
        }
    }
}

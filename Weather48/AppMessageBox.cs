namespace WeatherWidget48;

public static class AppMessageBox
{
    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        using var dialog = new Dialog
        {
            Text = caption,
            Message = text,
            Buttons = buttons,
            MessageIcon = icon
        };
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private sealed class Dialog : Form
    {
        public Dialog()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            Font = FontProvider.Create(12);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = new Padding(18);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 150);
        }

        public string Message { get; set; } = string.Empty;

        public MessageBoxButtons Buttons { get; set; }

        public MessageBoxIcon MessageIcon { get; set; }

        protected override void OnShown(EventArgs eventArgs)
        {
            base.OnShown(eventArgs);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var messageLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = Message,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = FontProvider.Create(12)
            };
            layout.Controls.Add(messageLabel, 0, 0);

            var buttonPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 12, 0, 0),
                WrapContents = false
            };
            foreach (var (text, result) in GetButtons(Buttons))
            {
                var button = new Button
                {
                    AutoSize = true,
                    DialogResult = result,
                    Font = FontProvider.Create(12),
                    Margin = new Padding(6, 0, 0, 0),
                    Text = text
                };
                buttonPanel.Controls.Add(button);
            }

            layout.Controls.Add(buttonPanel, 0, 1);
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Text = FormatIcon(MessageIcon),
                ForeColor = Color.DimGray,
                Font = FontProvider.Create(12),
                Margin = new Padding(0, 6, 0, 0)
            }, 0, 2);
            Controls.Add(layout);
            AcceptButton = buttonPanel.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.OK)
                ?? buttonPanel.Controls.OfType<Button>().FirstOrDefault();
            CancelButton = buttonPanel.Controls.OfType<Button>().FirstOrDefault(button => button.DialogResult == DialogResult.Cancel);
        }

        private static IEnumerable<(string Text, DialogResult Result)> GetButtons(MessageBoxButtons buttons)
        {
            switch (buttons)
            {
                case MessageBoxButtons.YesNo:
                    yield return ("是(&Y)", DialogResult.Yes);
                    yield return ("否(&N)", DialogResult.No);
                    break;
                case MessageBoxButtons.YesNoCancel:
                    yield return ("是(&Y)", DialogResult.Yes);
                    yield return ("否(&N)", DialogResult.No);
                    yield return ("取消", DialogResult.Cancel);
                    break;
                case MessageBoxButtons.OKCancel:
                    yield return ("确定", DialogResult.OK);
                    yield return ("取消", DialogResult.Cancel);
                    break;
                case MessageBoxButtons.RetryCancel:
                    yield return ("重试", DialogResult.Retry);
                    yield return ("取消", DialogResult.Cancel);
                    break;
                case MessageBoxButtons.AbortRetryIgnore:
                    yield return ("中止(&A)", DialogResult.Abort);
                    yield return ("重试(&R)", DialogResult.Retry);
                    yield return ("忽略(&I)", DialogResult.Ignore);
                    break;
                default:
                    yield return ("确定", DialogResult.OK);
                    break;
            }
        }

        private static string FormatIcon(MessageBoxIcon icon) => icon switch
        {
            MessageBoxIcon.Warning => "提示",
            MessageBoxIcon.Error => "错误",
            MessageBoxIcon.Information => "信息",
            _ => string.Empty
        };
    }
}

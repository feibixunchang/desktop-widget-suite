using System.Globalization;

namespace XiaoPuHeWeather;

public sealed class ColorPickerDialog : Form
{
    private readonly TextBox _hexTextBox;
    private readonly Panel _previewPanel;
    private readonly Label _previewLabel;

    public ColorPickerDialog(Color initialColor)
    {
        Text = "选择颜色";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        Font = FontProvider.Create(12);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(18);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(430, 235);

        var titleLabel = new Label
        {
            AutoSize = true,
            Text = "请输入十六进制颜色，例如 #1E90FF"
        };
        _hexTextBox = new TextBox
        {
            Text = ColorHtml.ToHex(initialColor),
            Width = 180
        };
        _hexTextBox.TextChanged += (_, _) => UpdatePreview();
        _previewPanel = new Panel
        {
            Height = 62,
            Dock = DockStyle.Fill,
            BackColor = initialColor
        };
        _previewLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "颜色预览"
        };
        _previewPanel.Controls.Add(_previewLabel);

        var okButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Text = "确定",
            Margin = new Padding(6, 0, 0, 0)
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Text = "取消",
            Margin = new Padding(6, 0, 0, 0)
        };
        okButton.Click += (_, _) => AcceptColor();
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 14, 0, 0)
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(titleLabel, 0, 0);
        layout.SetColumnSpan(titleLabel, 2);
        layout.Controls.Add(new Label { AutoSize = true, Text = "颜色值：" }, 0, 1);
        layout.Controls.Add(_hexTextBox, 1, 1);
        layout.Controls.Add(_previewPanel, 0, 2);
        layout.SetColumnSpan(_previewPanel, 2);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        UpdatePreview();
    }

    public Color SelectedColor { get; private set; }

    private void AcceptColor()
    {
        if (!TryParseColor(_hexTextBox.Text, out var color))
        {
            _hexTextBox.Focus();
            _hexTextBox.SelectAll();
            return;
        }

        SelectedColor = color;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdatePreview()
    {
        if (TryParseColor(_hexTextBox.Text, out var color))
        {
            SelectedColor = color;
            _previewPanel.BackColor = color;
            _previewLabel.ForeColor = GetContrastingTextColor(color);
        }
        else
        {
            _previewPanel.BackColor = Color.White;
            _previewLabel.ForeColor = Color.Red;
        }
    }

    private static bool TryParseColor(string text, out Color color)
    {
        var normalized = text.Trim();
        if (!normalized.StartsWith("#"))
        {
            normalized = "#" + normalized;
        }

        if (normalized.Length == 4)
        {
            normalized = $"#{normalized[1]}{normalized[1]}{normalized[2]}{normalized[2]}{normalized[3]}{normalized[3]}";
        }

        return ColorHtml.TryParse(normalized, out color)
            && uint.TryParse(normalized.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    }

    private static Color GetContrastingTextColor(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6 ? Color.Black : Color.White;
    }
}

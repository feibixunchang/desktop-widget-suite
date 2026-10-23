namespace WeatherWidget48;

public sealed class AppearanceDialog : Form
{
    private sealed record ColorOption(string Name, string Hex);

    private static readonly ColorOption[] ColorOptions =
    {
        new("白色", "#FFFFFF"),
        new("黑色", "#000000"),
        new("蓝色", "#1E90FF"),
        new("浅蓝色", "#87CEEB"),
        new("天蓝色", "#00BFFF"),
        new("青色", "#00CED1"),
        new("绿色", "#00C853"),
        new("黄色", "#FFD700"),
        new("橙色", "#FF8C00"),
        new("红色", "#FF3B30"),
        new("粉红色", "#FF69B4"),
        new("紫色", "#8A2BE2"),
        new("灰色", "#808080"),
        new("深蓝色", "#00008B")
    };

    private readonly NumericUpDown _firstLineSize;
    private readonly NumericUpDown _secondLineSize;
    private readonly Button _firstLineColorButton;
    private readonly Button _secondLineColorButton;
    private readonly List<Button> _firstLinePaletteButtons = new();
    private readonly List<Button> _secondLinePaletteButtons = new();
    private readonly Label _firstLinePreview;
    private readonly Label _secondLinePreview;
    private readonly Panel _previewPanel;
    private Font? _firstPreviewFont;
    private Font? _secondPreviewFont;

    public AppearanceDialog(AppSettings settings)
    {
        SelectedSettings = settings.Clone();
        Text = "字体与颜色设置";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        Font = FontProvider.Create(12);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScroll = true;
        Padding = new Padding(20);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 900);
        MinimumSize = new Size(860, 700);

        _firstLineSize = CreateSizeControl(settings.FirstLineFontSize, 12, 48);
        _secondLineSize = CreateSizeControl(settings.SecondLineFontSize, 8, 30);
        _firstLineColorButton = CreateCurrentColorButton(settings.FirstLineColor);
        _secondLineColorButton = CreateCurrentColorButton(settings.SecondLineColor);
        _firstLineColorButton.Click += (_, _) => ChooseColor(_firstLineColorButton, _firstLinePaletteButtons);
        _secondLineColorButton.Click += (_, _) => ChooseColor(_secondLineColorButton, _secondLinePaletteButtons);
        _firstLineSize.ValueChanged += (_, _) => ApplyPreview();
        _secondLineSize.ValueChanged += (_, _) => ApplyPreview();

        _firstLinePreview = new Label
        {
            AutoEllipsis = false,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "第一行：北京市　多云　25\u2103　湿度 42%　西南风 9km/h",
            TextAlign = ContentAlignment.MiddleCenter
        };
        _secondLinePreview = new Label
        {
            AutoEllipsis = false,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "第二行：今天 晴 14°/25°　明天 多云 15°/27°",
            TextAlign = ContentAlignment.MiddleCenter
        };
        _previewPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = Color.FromArgb(32, 36, 44),
            Padding = new Padding(8),
            MinimumSize = new Size(0, 170)
        };
        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        previewLayout.Controls.Add(_firstLinePreview, 0, 0);
        previewLayout.Controls.Add(_secondLinePreview, 0, 1);
        _previewPanel.Controls.Add(previewLayout);
        ApplyPreview();

        var settingsLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 4
        };
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < 4; index++)
        {
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        settingsLayout.Controls.Add(new Label { AutoSize = true, Text = "第一行字号（pt）" }, 0, 0);
        settingsLayout.Controls.Add(_firstLineSize, 1, 0);
        settingsLayout.Controls.Add(new Label { AutoSize = true, Text = "第二行字号（pt）" }, 0, 1);
        settingsLayout.Controls.Add(_secondLineSize, 1, 1);
        settingsLayout.Controls.Add(new Label { AutoSize = true, Text = "第一行颜色" }, 0, 2);
        settingsLayout.Controls.Add(CreateColorSelector(_firstLineColorButton, _firstLinePaletteButtons), 1, 2);
        settingsLayout.Controls.Add(new Label { AutoSize = true, Text = "第二行颜色" }, 0, 3);
        settingsLayout.Controls.Add(CreateColorSelector(_secondLineColorButton, _secondLinePaletteButtons), 1, 3);

        var fontLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = FontProvider.IsRequestedFamilyAvailable ? Color.DimGray : Color.DarkOrange,
            Margin = new Padding(0, 8, 0, 8),
            MaximumSize = new Size(880, 0),
            Text = FontProvider.IsRequestedFamilyAvailable
                ? "统一字体：方正宋黑_GBK（12pt）"
                : $"统一字体：方正宋黑_GBK 未安装，当前使用 {FontProvider.FamilyName}（12pt）",
            TextAlign = ContentAlignment.MiddleLeft
        };
        var previewHost = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            RowCount = 2
        };
        previewHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        previewHost.Controls.Add(fontLabel, 0, 0);
        previewHost.Controls.Add(_previewPanel, 0, 1);

        var okButton = CreateButton("确定", 90);
        okButton.DialogResult = DialogResult.OK;
        var cancelButton = CreateButton("取消", 90);
        cancelButton.DialogResult = DialogResult.Cancel;
        var resetButton = CreateButton("恢复默认", 110);
        resetButton.Click += (_, _) => ResetDefaults();
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0),
            WrapContents = true
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(resetButton);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(settingsLayout, 0, 0);
        root.Controls.Add(previewHost, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        okButton.Click += (_, _) => CaptureSettings();
    }

    public AppSettings SelectedSettings { get; private set; } = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _firstPreviewFont?.Dispose();
            _secondPreviewFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void CaptureSettings()
    {
        var current = SelectedSettings;
        current.FirstLineFontSize = (int)_firstLineSize.Value;
        current.SecondLineFontSize = (int)_secondLineSize.Value;
        current.FirstLineColor = ColorHtml.ToHex(_firstLineColorButton.Tag as string);
        current.SecondLineColor = ColorHtml.ToHex(_secondLineColorButton.Tag as string);
        current.TopMost = false;
    }

    private static NumericUpDown CreateSizeControl(int value, int minimum, int maximum)
    {
        return new NumericUpDown
        {
            AutoSize = true,
            Maximum = maximum,
            Minimum = minimum,
            Value = value,
            Width = 110
        };
    }

    private static Button CreateCurrentColorButton(string color)
    {
        var parsed = ParseColor(color);
        return new Button
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(3),
            MinimumSize = new Size(145, 40),
            BackColor = parsed,
            ForeColor = GetContrastingTextColor(parsed),
            Tag = ColorHtml.ToHex(parsed),
            Text = $"当前：{GetColorName(parsed)}"
        };
    }

    private Control CreateColorSelector(Button currentButton, List<Button> paletteButtons)
    {
        var selector = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            Dock = DockStyle.Fill,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = Padding.Empty,
            RowCount = 5
        };
        for (var column = 0; column < 5; column++)
        {
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }

        for (var row = 0; row < 5; row++)
        {
            selector.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        }

        currentButton.Dock = DockStyle.Fill;
        currentButton.AutoEllipsis = false;
        selector.Controls.Add(currentButton, 0, 0);
        selector.SetColumnSpan(currentButton, 5);

        var customButton = CreateButton("自定义颜色", 120);
        customButton.AutoSize = false;
        customButton.Dock = DockStyle.Fill;
        customButton.FlatStyle = FlatStyle.Flat;
        customButton.BackColor = Color.White;
        customButton.ForeColor = Color.Black;
        customButton.AutoEllipsis = false;
        customButton.Click += (_, _) => ChooseColor(currentButton, paletteButtons);
        selector.Controls.Add(customButton, 0, 1);
        selector.SetColumnSpan(customButton, 5);

        var index = 0;
        foreach (var option in ColorOptions)
        {
            var color = ParseColor(option.Hex);
            var optionButton = new Button
            {
                AutoEllipsis = false,
                AutoSize = false,
                BackColor = color,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                ForeColor = GetContrastingTextColor(color),
                Margin = new Padding(3),
                MinimumSize = new Size(120, 40),
                Tag = option.Hex,
                Text = option.Name,
                TextAlign = ContentAlignment.MiddleCenter
            };
            optionButton.FlatAppearance.BorderColor = Color.Silver;
            optionButton.FlatAppearance.BorderSize = 1;
            optionButton.Click += (_, _) =>
            {
                SetColor(currentButton, option.Hex, paletteButtons);
                ApplyPreview();
            };
            paletteButtons.Add(optionButton);
            selector.Controls.Add(optionButton, index % 5, 2 + index / 5);
            index++;
        }

        SetColor(currentButton, currentButton.Tag as string, paletteButtons);
        return selector;
    }

    private void ChooseColor(Button button, List<Button> paletteButtons)
    {
        ChooseColorForSelector(button, paletteButtons);
    }

    private void ChooseColorForSelector(Button button, List<Button> paletteButtons)
    {
        using var dialog = new ColorPickerDialog(ParseColor(button.Tag as string));
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetColor(button, ColorHtml.ToHex(dialog.SelectedColor), paletteButtons);
        ApplyPreview();
    }

    private void ApplyPreview()
    {
        _firstPreviewFont?.Dispose();
        _secondPreviewFont?.Dispose();
        _firstPreviewFont = FontProvider.Create((float)_firstLineSize.Value);
        _secondPreviewFont = FontProvider.Create((float)_secondLineSize.Value);
        _firstLinePreview.Font = _firstPreviewFont;
        _secondLinePreview.Font = _secondPreviewFont;
        _firstLinePreview.ForeColor = ParseColor(_firstLineColorButton.Tag as string);
        _secondLinePreview.ForeColor = ParseColor(_secondLineColorButton.Tag as string);
    }

    private void ResetDefaults()
    {
        _firstLineSize.Value = 24;
        _secondLineSize.Value = 12;
        SetColor(_firstLineColorButton, "#42A5F5", _firstLinePaletteButtons);
        SetColor(_secondLineColorButton, "#FFFFFF", _secondLinePaletteButtons);
        ApplyPreview();
    }

    private static void SetColor(Button button, string? color, List<Button> paletteButtons)
    {
        var normalized = ColorHtml.ToHex(color);
        var parsed = ParseColor(normalized);
        button.Tag = normalized;
        button.BackColor = parsed;
        button.ForeColor = GetContrastingTextColor(parsed);
        button.Text = $"当前：{GetColorName(parsed)}";
        foreach (var paletteButton in paletteButtons)
        {
            var selected = string.Equals(paletteButton.Tag as string, normalized, StringComparison.OrdinalIgnoreCase);
            paletteButton.FlatAppearance.BorderColor = selected ? Color.Black : Color.Silver;
            paletteButton.FlatAppearance.BorderSize = selected ? 3 : 1;
        }
    }

    private static string GetColorName(Color color)
    {
        var normalized = ColorHtml.ToHex(color);
        return ColorOptions.FirstOrDefault(option =>
            string.Equals(option.Hex, normalized, StringComparison.OrdinalIgnoreCase))?.Name
            ?? "自定义颜色";
    }

    private static Color ParseColor(string? value)
    {
        return ColorHtml.TryParse(value, out var color) ? color : Color.Black;
    }

    private static Color GetContrastingTextColor(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6 ? Color.Black : Color.White;
    }

    private static Button CreateButton(string text, int width)
    {
        return new Button
        {
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            MinimumSize = new Size(width, 36),
            Text = text
        };
    }
}

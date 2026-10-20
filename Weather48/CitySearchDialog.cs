using System.Globalization;

namespace XiaoPuHeWeather;

public sealed class CitySearchDialog : Form
{
    private readonly WeatherApiClient _weatherApiClient;
    private readonly TextBox _queryTextBox;
    private readonly Button _searchButton;
    private readonly ListBox _resultListBox;
    private readonly TextBox _nameTextBox;
    private readonly TextBox _latitudeTextBox;
    private readonly TextBox _longitudeTextBox;
    private readonly Label _statusLabel;
    private CancellationTokenSource? _searchCancellation;
    private Task? _searchTask;
    private GeoLocation? _selectedLocation;
    private bool _settingLocation;
    private bool _manualLocationEdited;
    private string _lastSearchedQuery = string.Empty;
    private string _activeSearchQuery = string.Empty;

    public CitySearchDialog(WeatherApiClient weatherApiClient, GeoLocation currentLocation)
    {
        _weatherApiClient = weatherApiClient;
        Text = "更换城市或地点";
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
        ClientSize = new Size(920, 800);
        MinimumSize = new Size(760, 620);

        var queryPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        queryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queryPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var queryLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            Text = "城市或地点："
        };
        _queryTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
            MinimumSize = new Size(320, 30),
            Text = currentLocation.Name
        };
        _searchButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 2),
            MinimumSize = new Size(90, 34),
            Text = "搜索"
        };
        queryPanel.Controls.Add(queryLabel, 0, 0);
        queryPanel.Controls.Add(_queryTextBox, 1, 0);
        queryPanel.Controls.Add(_searchButton, 2, 0);

        _resultListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Height = 380,
            HorizontalScrollbar = true,
            IntegralHeight = false,
            Margin = new Padding(0, 10, 0, 8),
            SelectionMode = SelectionMode.One
        };
        _resultListBox.DisplayMember = nameof(GeoLocation.DisplayName);
        _resultListBox.SelectedIndexChanged += (_, _) => SelectResult();

        _statusLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(860, 0),
            Text = "可以直接输入城市名搜索，也可以手动填写经纬度。"
        };

        _nameTextBox = CreateField(currentLocation.Name);
        _latitudeTextBox = CreateField(currentLocation.Latitude.ToString("F6", CultureInfo.InvariantCulture));
        _longitudeTextBox = CreateField(currentLocation.Longitude.ToString("F6", CultureInfo.InvariantCulture));
        _nameTextBox.TextChanged += MarkManualLocation;
        _latitudeTextBox.TextChanged += MarkManualLocation;
        _longitudeTextBox.TextChanged += MarkManualLocation;
        _queryTextBox.TextChanged += (_, _) => QueryChanged();

        var detailsGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 4, 0, 8),
            Padding = new Padding(12),
            Text = "地点设置"
        };
        var detailsLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            RowCount = 1
        };
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsLayout.Controls.Add(CreateFieldPanel("显示名称", _nameTextBox), 0, 0);
        detailsLayout.Controls.Add(CreateFieldPanel("纬度", _latitudeTextBox), 1, 0);
        detailsLayout.Controls.Add(CreateFieldPanel("经度", _longitudeTextBox), 2, 0);
        detailsGroup.Controls.Add(detailsLayout);

        var noteLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(860, 0),
            Text = "搜索会优先使用内置常用地点；找不到时可手动输入名称、纬度和经度。"
        };

        var applyButton = CreateButton("应用选中地点", 120);
        var okButton = CreateButton("确定并刷新", 120);
        var cancelButton = CreateButton("取消", 90);
        cancelButton.DialogResult = DialogResult.Cancel;
        var defaultButton = CreateButton("恢复安宁市", 120);
        applyButton.Click += async (_, _) => await ApplyLocationAsync();
        okButton.Click += async (_, _) => await ApplyLocationAsync();
        defaultButton.Click += (_, _) => SetLocation(GeoLocation.Default);
        _searchButton.Click += async (_, _) => await SearchAsync();
        _queryTextBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            await SearchAsync();
        };
        _resultListBox.MouseDoubleClick += async (_, _) => await ApplyLocationAsync();

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 4, 0, 0),
            WrapContents = true
        };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(applyButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(defaultButton);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 390));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(queryPanel, 0, 0);
        root.Controls.Add(_resultListBox, 0, 1);
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(detailsGroup, 0, 3);
        root.Controls.Add(noteLabel, 0, 4);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        FormClosed += (_, _) => _searchCancellation?.Cancel();
        SetLocation(currentLocation);
    }

    public GeoLocation? SelectedLocation { get; private set; }

    private async Task SearchAsync()
    {
        var query = _queryTextBox.Text.Trim();
        if (_searchTask is not null)
        {
            if (string.Equals(_activeSearchQuery, query, StringComparison.Ordinal))
            {
                await _searchTask;
                return;
            }

            _searchCancellation?.Cancel();
        }

        if (query.Length == 0)
        {
            _statusLabel.Text = "请输入城市或地点名称。";
            return;
        }

        var searchTask = SearchCoreAsync(query);
        _searchTask = searchTask;
        try
        {
            await searchTask;
        }
        finally
        {
            if (ReferenceEquals(_searchTask, searchTask))
            {
                _searchTask = null;
            }
        }
    }

    private async Task SearchCoreAsync(string query)
    {
        _lastSearchedQuery = query;
        _activeSearchQuery = query;
        _searchButton.Enabled = false;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var searchCancellation = new CancellationTokenSource();
        _searchCancellation = searchCancellation;
        try
        {
            var localResults = LocationCatalog.Search(query);
            var exactLocalResult = localResults.FirstOrDefault(location =>
                string.Equals(location.Name, query, StringComparison.Ordinal)
                || (query is "安宁市" or "安宁市村"
                    && location.Name == "安宁市"));
            if (exactLocalResult is not null)
            {
                SetSearchResults(new[] { exactLocalResult }, $"已使用内置地点：{exactLocalResult.DisplayName}");
                return;
            }

            SetSearchResults(localResults, localResults.Count > 0
                ? "已显示内置常用地点，正在补充网络结果…"
                : "正在搜索网络地点…");

            IReadOnlyList<GeoLocation> remoteResults;
            string? remoteError = null;
            try
            {
                remoteResults = await _weatherApiClient.SearchLocationsAsync(query, searchCancellation.Token);
            }
            catch (OperationCanceledException) when (searchCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                remoteResults = Array.Empty<GeoLocation>();
                remoteError = exception.Message;
            }

            if (searchCancellation.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            var results = localResults
                .Concat(remoteResults)
                .GroupBy(location => $"{location.Name}|{location.Latitude:F5}|{location.Longitude:F5}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(location => string.Equals(location.Name, query, StringComparison.Ordinal))
                .ThenByDescending(location => localResults.Contains(location))
                .ThenBy(location => location.Name, StringComparer.Ordinal)
                .Take(50)
                .ToArray();
            SetSearchResults(results, remoteError is null
                ? results.Length > 0
                    ? $"找到 {results.Length} 个结果，已选中第一项。"
                    : "未找到匹配地点，请手动填写名称和经纬度。"
                : $"已加载 {results.Length} 个结果；网络搜索失败：{remoteError}");
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, searchCancellation))
            {
                _searchCancellation = null;
                _activeSearchQuery = string.Empty;
            }

            searchCancellation.Dispose();
            if (!IsDisposed)
            {
                _searchButton.Enabled = true;
            }
        }
    }

    private void SetSearchResults(IReadOnlyList<GeoLocation> results, string status)
    {
        _resultListBox.Items.Clear();
        foreach (var result in results)
        {
            _resultListBox.Items.Add(result);
        }

        if (results.Count > 0)
        {
            _resultListBox.SelectedIndex = 0;
        }

        _statusLabel.Text = status;
    }

    private async Task ApplyLocationAsync()
    {
        if (_searchTask is not null)
        {
            await _searchTask;
        }

        var query = _queryTextBox.Text.Trim();
        if (!_manualLocationEdited
            && query.Length > 0
            && (!string.Equals(query, _lastSearchedQuery, StringComparison.Ordinal)
                || _selectedLocation is null))
        {
            await SearchAsync();
        }

        if (_resultListBox.SelectedItem is GeoLocation selected)
        {
            SetLocation(selected);
        }
        else if (!_manualLocationEdited && _selectedLocation is null)
        {
            AppMessageBox.Show(
                this,
                "没有找到可应用的地点，请先搜索、选择结果，或手动填写经纬度。",
                "更换地点",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ValidateAndAccept();
    }

    private void QueryChanged()
    {
        if (_settingLocation)
        {
            return;
        }

        _searchCancellation?.Cancel();
        _resultListBox.Items.Clear();
        _selectedLocation = null;
        _lastSearchedQuery = string.Empty;
        _statusLabel.Text = "请输入新的城市或地点名称。";
    }

    private void SelectResult()
    {
        if (_resultListBox.SelectedItem is GeoLocation location)
        {
            SetLocation(location);
        }
    }

    private void MarkManualLocation(object? sender, EventArgs eventArgs)
    {
        if (!_settingLocation)
        {
            _selectedLocation = null;
            _manualLocationEdited = true;
            _statusLabel.Text = "已手动修改地点信息，可直接点击“确定并刷新”。";
        }
    }

    private void SetLocation(GeoLocation location)
    {
        _settingLocation = true;
        try
        {
            _selectedLocation = location;
            _manualLocationEdited = false;
            _lastSearchedQuery = location.Name;
            _nameTextBox.Text = location.Name;
            _latitudeTextBox.Text = location.Latitude.ToString("F6", CultureInfo.InvariantCulture);
            _longitudeTextBox.Text = location.Longitude.ToString("F6", CultureInfo.InvariantCulture);
            _queryTextBox.Text = location.Name;
        }
        finally
        {
            _settingLocation = false;
        }
    }

    private void ValidateAndAccept()
    {
        var name = _nameTextBox.Text.Trim();
        if (name.Length == 0)
        {
            AppMessageBox.Show(this, "请输入地点名称。", "更换地点", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!double.TryParse(_latitudeTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !Compat.IsFinite(latitude)
            || latitude is < -90 or > 90)
        {
            AppMessageBox.Show(this, "请输入 -90 到 90 之间的纬度。", "更换地点", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!double.TryParse(_longitudeTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || !Compat.IsFinite(longitude)
            || longitude is < -180 or > 180)
        {
            AppMessageBox.Show(this, "请输入 -180 到 180 之间的经度。", "更换地点", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _selectedLocation;
        SelectedLocation = new GeoLocation(
            name,
            selected?.Admin1 ?? "中国",
            selected?.Admin2 ?? string.Empty,
            selected?.Country ?? "中国",
            latitude,
            longitude);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static TextBox CreateField(string text)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
            MinimumSize = new Size(160, 30),
            Text = text
        };
    }

    private static Control CreateFieldPanel(string text, Control field)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
            Text = text
        }, 0, 0);
        panel.Controls.Add(field, 0, 1);
        return panel;
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

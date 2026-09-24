using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class DormElectricityWidgetContent : UserControl, IWidgetContent, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly SettingsService? _settings;
    private readonly DormElectricityService _service = new();
    private readonly DispatcherQueueTimer? _timer;
    private readonly TextBlock _locationText = new() { FontSize = 12, Opacity = 0.75 };
    private readonly TextBlock _balanceText = new() { FontSize = 34, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _recordedAtText = new() { FontSize = 11, Opacity = 0.65 };
    private readonly TextBlock _statusText = new() { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _historyHost = new();
    private readonly ComboBox _recordSelector = new()
    {
        MinWidth = 104,
        MaxWidth = 118,
        FontSize = 11,
        HorizontalAlignment = HorizontalAlignment.Right
    };
    private readonly Button _settingsButton = new() { Content = "⚙", Padding = new Thickness(7, 3, 7, 3) };
    private readonly Button _refreshButton = new() { Content = "↻", Padding = new Thickness(7, 3, 7, 3) };
    private Canvas? _chartCanvas;
    private TextBlock? _chartMaxText;
    private TextBlock? _chartFirstDate;
    private TextBlock? _chartLastDate;
    private DormElectricitySnapshot? _snapshot;
    private DormElectricityQuery? _lastQuery;
    private DateTime _lastRefreshAt;
    private bool _showPayments;
    private DormElectricityUsageLayout _usageLayout = DormElectricityUsageLayout.Vertical;
    private bool _isRefreshing;
    private bool _pendingRefresh;
    private bool _disposed;

    public DormElectricityWidgetContent(
        WidgetConfig config,
        LocalizationService localization,
        SettingsService? settings)
    {
        Config = config;
        _localization = localization;
        _settings = settings;
        Content = CreateView();

        DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
        if (queue is not null)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMinutes(60);
            _timer.IsRepeating = true;
            _timer.Tick += Timer_Tick;
        }
        if (_settings is not null)
        {
            _settings.SettingsChanged += Settings_SettingsChanged;
        }
        _localization.LanguageChanged += Localization_LanguageChanged;
        SizeChanged += Widget_SizeChanged;
        UpdateLabels();
    }

    public WidgetConfig Config { get; }
    public string WidgetId => Config.Id;
    public WidgetKind WidgetKind => Config.WidgetKind;
    public FrameworkElement View => this;

    public Task InitializeAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }
        if (_isRefreshing)
        {
            _pendingRefresh = true;
            return;
        }

        DormElectricityQuery? query = GetQuery();
        if (query is null)
        {
            _snapshot = null;
            _lastQuery = null;
            _locationText.Text = string.Empty;
            _balanceText.Text = "—";
            _recordedAtText.Text = string.Empty;
            _statusText.Text = T("DormElectricity.ConfigurePrompt");
            RenderRows();
            return;
        }

        _isRefreshing = true;
        _locationText.Text = query.Location.BuildingName + " " + query.Location.RoomName;
        _statusText.Text = T("DormElectricity.Refreshing");
        try
        {
            DormElectricitySnapshot snapshot = await _service.GetSnapshotAsync(
                query.Location, DateTime.Now, query.UsagePeriod, query.PaymentPeriod);
            if (_disposed)
            {
                return;
            }
            if (GetQuery() != query)
            {
                _pendingRefresh = true;
                return;
            }
            _snapshot = snapshot;
            _lastQuery = query;
            _lastRefreshAt = DateTime.Now;
            DormElectricityDay? latest = snapshot.Days.LastOrDefault();
            _balanceText.Text = latest is null
                ? "—"
                : latest.RemainingKwh.ToString("0.##") + " " + T("DormElectricity.KwhUnit");
            _recordedAtText.Text = latest is null
                ? T("DormElectricity.NoUsageData")
                : _localization.Format("DormElectricity.BalanceRecordedAt",
                    latest.RecordedAt.ToString("yyyy-MM-dd HH:mm"));
            _statusText.Text = _localization.Format(
                "DormElectricity.UpdatedAt", _lastRefreshAt.ToString("HH:mm"));
            UpdateLabels();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _statusText.Text = ex is HttpRequestException or TaskCanceledException
                ? T("DormElectricity.NetworkError")
                : ex.Message;
        }
        finally
        {
            _isRefreshing = false;
            if (_pendingRefresh && !_disposed)
            {
                _pendingRefresh = false;
                _ = RefreshAsync();
            }
        }
    }

    public void ApplyAppearance() { }
    public void OnActivated() { }
    public void OnDeactivated() { }

    public void OnWindowVisibilityChanged(bool visible)
    {
        if (visible)
        {
            _timer?.Start();
            if (DateTime.Now - _lastRefreshAt > TimeSpan.FromMinutes(60))
            {
                _ = RefreshAsync();
            }
        }
        else
        {
            _timer?.Stop();
        }
    }

    private FrameworkElement CreateView()
    {
        var root = new Grid { Padding = new Thickness(14, 11, 14, 11), RowSpacing = 9 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new Grid { ColumnSpacing = 6 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _locationText.VerticalAlignment = VerticalAlignment.Center;
        _locationText.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(_locationText);
        _recordSelector.Items.Add(new ComboBoxItem());
        _recordSelector.Items.Add(new ComboBoxItem());
        _recordSelector.SelectedIndex = 0;
        _recordSelector.SelectionChanged += (_, _) =>
        {
            _showPayments = _recordSelector.SelectedIndex == 1;
            RenderRows();
        };
        Grid.SetColumn(_recordSelector, 1);
        top.Children.Add(_recordSelector);
        ToolTipService.SetToolTip(_settingsButton, T("DormElectricity.Settings"));
        _settingsButton.Click += (_, _) => App.Current.ShowSettings("DormElectricitySettings");
        Grid.SetColumn(_settingsButton, 2);
        top.Children.Add(_settingsButton);
        ToolTipService.SetToolTip(_refreshButton, T("DormElectricity.Refresh"));
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        Grid.SetColumn(_refreshButton, 3);
        top.Children.Add(_refreshButton);
        root.Children.Add(top);

        var balanceCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 120, 120, 120)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9, 12, 9)
        };
        var balanceStack = new StackPanel { Spacing = 1 };
        var balanceLabel = new TextBlock { Name = "BalanceLabel", FontSize = 12, Opacity = 0.7 };
        balanceStack.Children.Add(balanceLabel);
        balanceStack.Children.Add(_balanceText);
        balanceStack.Children.Add(_recordedAtText);
        balanceCard.Child = balanceStack;
        Grid.SetRow(balanceCard, 1);
        root.Children.Add(balanceCard);

        Grid.SetRow(_historyHost, 2);
        root.Children.Add(_historyHost);
        Grid.SetRow(_statusText, 3);
        root.Children.Add(_statusText);
        return root;
    }

    private void UpdateLabels()
    {
        ((ComboBoxItem)_recordSelector.Items[0]).Content = T("DormElectricity.UsageTab");
        ((ComboBoxItem)_recordSelector.Items[1]).Content = T("DormElectricity.PaymentsTab");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            _recordSelector, T("DormElectricity.RecordType"));
        if (Content is Grid root && root.Children.OfType<Border>().FirstOrDefault()?.Child is StackPanel stack &&
            stack.Children.FirstOrDefault() is TextBlock label)
        {
            label.Text = T("DormElectricity.Balance");
        }
        RenderRows();
    }

    private void Widget_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool shortWidget = e.NewSize.Height < 320;
        if (Content is Grid root)
        {
            root.RowSpacing = shortWidget ? 6 : 9;
        }
        _balanceText.FontSize = shortWidget ? 27 : 34;
        _recordedAtText.Visibility = shortWidget ? Visibility.Collapsed : Visibility.Visible;
        _statusText.Visibility = shortWidget ? Visibility.Collapsed : Visibility.Visible;
        _locationText.Visibility = e.NewSize.Width < 230 ? Visibility.Collapsed : Visibility.Visible;
        _refreshButton.Visibility = e.NewSize.Width < 260 ? Visibility.Collapsed : Visibility.Visible;
        _settingsButton.Visibility = e.NewSize.Width < 170 ? Visibility.Collapsed : Visibility.Visible;
        DormElectricityUsageLayout layout = GetUsageLayout(e.NewSize.Width, e.NewSize.Height);
        if (layout != _usageLayout)
        {
            _usageLayout = layout;
            if (!_showPayments)
            {
                RenderRows();
            }
        }
    }

    private static DormElectricityUsageLayout GetUsageLayout(double width, double height)
    {
        if (width >= 420 && height >= 340 && width * height >= 190_000)
        {
            return DormElectricityUsageLayout.Chart;
        }
        return width >= height * 1.3
            ? DormElectricityUsageLayout.Horizontal
            : DormElectricityUsageLayout.Vertical;
    }

    private void RenderRows()
    {
        _historyHost.Children.Clear();
        _chartCanvas = null;
        if (_snapshot is null)
        {
            _historyHost.Children.Add(new TextBlock
            {
                Text = T("DormElectricity.ConfigurePrompt"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });
            return;
        }

        if (_showPayments)
        {
            _historyHost.Children.Add(CreatePaymentsView());
        }
        else if (_snapshot.Days.Count == 0)
        {
            _historyHost.Children.Add(new TextBlock { Text = T("DormElectricity.NoUsageData"), Opacity = 0.7 });
        }
        else if (_usageLayout == DormElectricityUsageLayout.Chart)
        {
            _historyHost.Children.Add(CreateChartView());
        }
        else
        {
            _historyHost.Children.Add(CreateCompactUsageView());
        }
    }

    private Grid CreateHistoryFrame(string title)
    {
        var frame = new Grid { RowSpacing = 6 };
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frame.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return frame;
    }

    private Grid CreatePaymentsView()
    {
        string period = GetQuery()?.PaymentPeriod ?? DormElectricityPeriods.OneYear;
        Grid frame = CreateHistoryFrame(T("DormElectricity.PaymentsTab") + " · " +
            T(DormElectricityPeriods.LabelKey(period)));
        var rows = new StackPanel { Spacing = 5 };
        AddRow(rows, T("DormElectricity.Date"), T("DormElectricity.Purchased"),
            T("DormElectricity.Amount"), header: true);
        foreach (DormElectricityPayment payment in _snapshot!.Payments)
        {
            AddRow(rows, payment.PaidAt.ToString("yyyy-MM-dd HH:mm"),
                payment.PurchasedKwh.ToString("0.##"), payment.AmountYuan.ToString("0.##"));
        }
        if (_snapshot.Payments.Count == 0)
        {
            rows.Children.Add(new TextBlock { Text = T("DormElectricity.NoPayments"), Opacity = 0.7 });
        }
        var scroll = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        frame.Children.Add(scroll);
        return frame;
    }

    private Grid CreateCompactUsageView()
    {
        string period = GetQuery()?.UsagePeriod ?? DormElectricityPeriods.SevenDays;
        (DateTime start, DateTime end) = GetUsageDateRange();
        Dictionary<DateTime, DormElectricityDay> byDate = _snapshot!.Days
            .ToDictionary(day => day.RecordedAt.Date);
        Grid frame = CreateHistoryFrame(T("DormElectricity.DailyUsage") + " · " +
            T(DormElectricityPeriods.LabelKey(period)));
        bool horizontal = _usageLayout == DormElectricityUsageLayout.Horizontal;
        var items = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = 6
        };
        for (DateTime dateValue = end; dateValue >= start; dateValue = dateValue.AddDays(-1))
        {
            byDate.TryGetValue(dateValue, out DormElectricityDay? day);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(18, 120, 120, 120)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 6, 9, 6),
                Width = horizontal ? 82 : double.NaN
            };
            var date = new TextBlock
            {
                Text = dateValue.ToString("MM-dd"),
                FontSize = 11,
                Opacity = 0.7
            };
            var value = new TextBlock
            {
                Text = day?.UsedKwh?.ToString("0.##") ?? "—",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            if (horizontal)
            {
                var column = new StackPanel { Spacing = 1 };
                column.Children.Add(date);
                column.Children.Add(value);
                card.Child = column;
            }
            else
            {
                var row = new Grid { ColumnSpacing = 5 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(date);
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                card.Child = row;
            }
            ToolTipService.SetToolTip(card, dateValue.ToString("yyyy-MM-dd") + " · " +
                (day?.UsedKwh?.ToString("0.##") ?? "—") + " " + T("DormElectricity.KwhUnit"));
            items.Children.Add(card);
        }
        var scroll = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);
        frame.Children.Add(scroll);
        return frame;
    }

    private Grid CreateChartView()
    {
        string period = GetQuery()?.UsagePeriod ?? DormElectricityPeriods.SevenDays;
        Grid frame = CreateHistoryFrame(T("DormElectricity.DailyUsage") + " · " +
            T(DormElectricityPeriods.LabelKey(period)));
        var chart = new Grid { ColumnSpacing = 7, RowSpacing = 4 };
        chart.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chart.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        chart.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scale = new Grid { MinWidth = 32 };
        _chartMaxText = new TextBlock { FontSize = 10, Opacity = 0.65 };
        scale.Children.Add(_chartMaxText);
        scale.Children.Add(new TextBlock
        {
            Text = "0",
            FontSize = 10,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Bottom
        });
        chart.Children.Add(scale);
        _chartCanvas = new Canvas { MinHeight = 110 };
        _chartCanvas.SizeChanged += (_, _) => DrawChart();
        Grid.SetColumn(_chartCanvas, 1);
        chart.Children.Add(_chartCanvas);
        var dates = new Grid();
        dates.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dates.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _chartFirstDate = new TextBlock { FontSize = 10, Opacity = 0.65 };
        _chartLastDate = new TextBlock { FontSize = 10, Opacity = 0.65 };
        dates.Children.Add(_chartFirstDate);
        Grid.SetColumn(_chartLastDate, 1);
        dates.Children.Add(_chartLastDate);
        Grid.SetRow(dates, 1);
        Grid.SetColumn(dates, 1);
        chart.Children.Add(dates);
        Grid.SetRow(chart, 1);
        frame.Children.Add(chart);
        return frame;
    }

    private void DrawChart()
    {
        Canvas? canvas = _chartCanvas;
        IReadOnlyList<DormElectricityDay>? days = _snapshot?.Days;
        if (canvas is null || days is null || days.Count == 0 ||
            canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
        {
            return;
        }

        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        (DateTime start, DateTime end) = GetUsageDateRange();
        int dayCount = (end - start).Days + 1;
        if (dayCount <= 0)
        {
            return;
        }
        Dictionary<DateTime, DormElectricityDay> byDate = days.ToDictionary(day => day.RecordedAt.Date);
        double maximum = Math.Max(1, (double)days.Where(day => day.UsedKwh.HasValue)
            .Select(day => day.UsedKwh!.Value).DefaultIfEmpty(0).Max() * 1.1);
        _chartMaxText!.Text = maximum.ToString("0.#") + " " + T("DormElectricity.KwhUnit");
        _chartFirstDate!.Text = start.ToString("MM-dd");
        _chartLastDate!.Text = end.ToString("MM-dd");
        canvas.Children.Clear();
        var gridBrush = new SolidColorBrush(Color.FromArgb(45, 125, 125, 125));
        for (int index = 0; index <= 3; index++)
        {
            double y = height * index / 3;
            canvas.Children.Add(new Line
            {
                X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1
            });
        }

        var accent = new SolidColorBrush(Color.FromArgb(255, 48, 139, 221));
        var run = new List<Point>();
        void AddRun()
        {
            if (run.Count > 1)
            {
                var line = new Polyline { Stroke = accent, StrokeThickness = 2.5 };
                foreach (Point point in run)
                {
                    line.Points.Add(point);
                }
                canvas.Children.Add(line);
            }
            else if (run.Count == 1 && dayCount > 31)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = accent };
                Canvas.SetLeft(dot, run[0].X - 3.5);
                Canvas.SetTop(dot, run[0].Y - 3.5);
                canvas.Children.Add(dot);
            }
            run.Clear();
        }

        for (int index = 0; index < dayCount; index++)
        {
            DateTime date = start.AddDays(index);
            if (!byDate.TryGetValue(date, out DormElectricityDay? day) ||
                day.UsedKwh is not decimal used)
            {
                AddRun();
                continue;
            }
            double x = dayCount == 1 ? width / 2 : 4 + index * Math.Max(0, width - 8) / (dayCount - 1);
            double y = height - 4 - Math.Clamp((double)used / maximum, 0, 1) * Math.Max(0, height - 8);
            run.Add(new Point(x, y));
            if (dayCount <= 31)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = accent };
                Canvas.SetLeft(dot, x - 3.5);
                Canvas.SetTop(dot, y - 3.5);
                ToolTipService.SetToolTip(dot, date.ToString("yyyy-MM-dd") +
                    " · " + used.ToString("0.##") + " " + T("DormElectricity.KwhUnit"));
                canvas.Children.Add(dot);
            }
        }
        AddRun();
    }

    private (DateTime Start, DateTime End) GetUsageDateRange()
    {
        DateTime today = _lastRefreshAt == default ? DateTime.Today : _lastRefreshAt.Date;
        string period = GetQuery()?.UsagePeriod ?? DormElectricityPeriods.SevenDays;
        return (DormElectricityPeriods.StartDate(period, today), today.AddDays(-1));
    }

    private void AddRow(StackPanel rows, string date, string middle, string right, bool header = false)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(3, 3, 3, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        AddCell(grid, date, 0, header);
        AddCell(grid, middle, 1, header);
        AddCell(grid, right, 2, header);
        rows.Children.Add(grid);
    }

    private static void AddCell(Grid grid, string value, int column, bool header)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = header ? 11 : 12,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Opacity = header ? 0.6 : 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right
        };
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private DormElectricityQuery? GetQuery()
    {
        AppSettings? settings = _settings?.Settings;
        if (settings is null || string.IsNullOrWhiteSpace(settings.DormElectricity.DormElectricityBuildingId) ||
            string.IsNullOrWhiteSpace(settings.DormElectricity.DormElectricityRoomName))
        {
            return null;
        }
        return new DormElectricityQuery(
            new DormElectricityLocation(
                settings.DormElectricity.DormElectricityClient,
                settings.DormElectricity.DormElectricityBuildingId,
                settings.DormElectricity.DormElectricityBuildingName,
                settings.DormElectricity.DormElectricityRoomName),
            DormElectricityPeriods.Normalize(settings.DormElectricity.DormElectricityUsagePeriod,
                DormElectricityPeriods.SevenDays),
            DormElectricityPeriods.Normalize(settings.DormElectricity.DormElectricityPaymentPeriod,
                DormElectricityPeriods.OneYear));
    }

    private string T(string key) => _localization.T(key);

    private void Timer_Tick(DispatcherQueueTimer sender, object args) => _ = RefreshAsync();

    private void Settings_SettingsChanged()
    {
        DormElectricityQuery? query = GetQuery();
        if (query != _lastQuery || _isRefreshing)
        {
            UpdateLabels();
            _ = RefreshAsync();
        }
    }

    private void Localization_LanguageChanged() => UpdateLabels();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer?.Stop();
        if (_settings is not null)
        {
            _settings.SettingsChanged -= Settings_SettingsChanged;
        }
        _localization.LanguageChanged -= Localization_LanguageChanged;
    }

    private sealed record DormElectricityQuery(
        DormElectricityLocation Location, string UsagePeriod, string PaymentPeriod);

    private enum DormElectricityUsageLayout
    {
        Vertical,
        Horizontal,
        Chart
    }
}

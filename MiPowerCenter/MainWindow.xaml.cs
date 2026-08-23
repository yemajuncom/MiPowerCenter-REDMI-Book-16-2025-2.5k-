using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MiPowerCenter.Models;
using MiPowerCenter.Services;
using WF = System.Windows.Forms;

namespace MiPowerCenter;

public partial class MainWindow : Window
{
    private XiaomiModuleAdapter _adapter;
    private readonly DispatcherTimer _batteryTimer;
    private readonly ObservableCollection<ModeItem> _modes = new();

    private bool _onAc;
    private bool _standardCharger = true;
    private bool _dryRun;
    private PerformanceMode? _currentMode;
    private PerformanceMode? _pendingSet;
    private bool? _chargeProtect;
    private int? _chargingState;
    private bool _chargeProtectSending;
    private int _chargeThreshold = 1;

    private static readonly (int Firmware, int Percent)[] ThresholdMap =
    {
        // 今日实测（AC 连续充电蹲点）：mode5 停 70%、mode6/7/8 停 ≤68%、mode4 停 90%、mode1-3 为高档(~90-100)。
        // 本机实测可用档仅 70% 与 90% 两档。
        (5, 70), (4, 90)
    };

    private static int PercentToFirmware(int pct) => ThresholdMap.FirstOrDefault(t => t.Percent == pct).Firmware;
    private static int FirmwareToPercent(int mode) => ThresholdMap.FirstOrDefault(t => t.Firmware == mode).Percent;

    private WF.NotifyIcon _tray;
    private bool _allowExit;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        ModeList.ItemsSource = _modes;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        InitTray();

        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _batteryTimer.Tick += (_, _) => RefreshBattery();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppLog.Write("OnLoaded start");
        RefreshBattery();
        _batteryTimer.Start();

        string dir = XiaomiModuleAdapter.FindXiaomiDir();
        AppLog.Write("FindXiaomiDir -> " + (dir ?? "null"));
        if (dir == null)
        {
            EnterDryRun("未检测到小米服务模块，性能模式不可用");
            return;
        }

        _adapter = new XiaomiModuleAdapter();
        _adapter.Success += OnModuleSuccess;
        _adapter.Failure += OnModuleFailure;

        // 性能模式依赖 Timi 运行时（MiDeviceService→MiScenarioRecognition 管道），
        // 卸载管家后可能被停止/禁用，这里先恢复。
        XiaomiModuleAdapter.EnsureTimiServices();

        try
        {
            _adapter.Init(dir);
        }
        catch (Exception ex)
        {
            AppLog.Write("Init exception: " + ex.Message + " " + ex);
            EnterDryRun("启用小米服务失败：" + ex.Message);
            return;
        }
        if (!_adapter.IsReady)
        {
            EnterDryRun("无法启动小米服务模块");
            return;
        }
        SetServiceStatus(true, "已连接小米服务");

        _adapter.Execute("{\"method\":\"get_workLoad_mode\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"get_workLoad_mode_decepticon_enable\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"register_ac_power_status\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"register_workLoad_mode_change\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"register_battery_notify\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"get_charging_protect\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"get_charging_threshold\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"get_charging_state\",\"params\":{}}");
        _adapter.Execute("{\"method\":\"get_battery_info\",\"params\":{}}");

        if (Environment.GetEnvironmentVariable("MIPC_SELFTEST") == "1")
            StartSelfTest();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        _batteryTimer.Stop();
        _tray?.Dispose();
    }

    private void InitTray()
    {
        _tray = new WF.NotifyIcon
        {
            Text = "电池性能管理",
            Visible = false
        };
        _tray.Icon = CreateTrayIcon();
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowFromTray());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });
        menu.ShowImageMargin = false;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void HideToTray()
    {
        _tray.Visible = true;
        ShowInTaskbar = false;
        Hide();
        if (!_trayHintShown)
        {
            _tray.ShowBalloonTip(2000, "电池性能管理", "已最小化到后台运行，点击托盘图标恢复", WF.ToolTipIcon.Info);
            _trayHintShown = true;
        }
    }

    private void ShowFromTray()
    {
        _tray.Visible = false;
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            const int r = 6;
            path.AddArc(1, 1, r, r, 180, 90);
            path.AddArc(25, 1, r, r, 270, 90);
            path.AddArc(25, 25, r, r, 0, 90);
            path.AddArc(1, 25, r, r, 90, 90);
            path.CloseFigure();
            using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 105, 0));
            g.FillPath(fill, path);
            System.Drawing.PointF[] bolt =
            {
                new System.Drawing.PointF(18, 6), new System.Drawing.PointF(11, 18), new System.Drawing.PointF(15, 18),
                new System.Drawing.PointF(13, 26), new System.Drawing.PointF(21, 13), new System.Drawing.PointF(17, 13)
            };
            using var boltBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.FillPolygon(boltBrush, bolt);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void StartSelfTest()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            AppLog.Write("SELFTEST current=" + (_currentMode?.ToString() ?? "null"));
            int target = (_currentMode == PerformanceMode.Smart)
                ? (_onAc ? (int)PerformanceMode.Fierce : (int)PerformanceMode.PowerSave)
                : (int)PerformanceMode.Smart;
            _pendingSet = (PerformanceMode)target;
            AppLog.Write("SELFTEST set target=" + target);
            _adapter.Execute($"{{\"method\":\"set_workLoad_mode\",\"params\":{{\"mode\":{target}}}}}");
        };
        t.Start();
        var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        t2.Tick += (_, _) =>
        {
            t2.Stop();
            int restore = _onAc ? (int)PerformanceMode.Fierce : (int)PerformanceMode.PowerSave;
            _pendingSet = (PerformanceMode)restore;
            AppLog.Write("SELFTEST after-set current=" + (_currentMode?.ToString() ?? "null"));
            AppLog.Write("SELFTEST restore target=" + restore);
            _adapter.Execute($"{{\"method\":\"set_workLoad_mode\",\"params\":{{\"mode\":{restore}}}}}");
        };
        t2.Start();
    }

    private void EnterDryRun(string message)
    {
        _dryRun = true;
        SetServiceStatus(false, message);
        ModeHintText.Text = message;
        _modes.Clear();
        _currentMode = PerformanceMode.Smart;
        ModeNameText.Text = ModeInfo.GetName(_currentMode.Value);
    }

    private void SetServiceStatus(bool ok, string text)
    {
        SvcDot.Fill = ok
            ? new SolidColorBrush(Color.FromRgb(0x10, 0xC5, 0x50))
            : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0x00));
        SvcText.Text = text;
        SvcDot.ToolTip = text;
    }

    private void RefreshBattery()
    {
        SystemPowerStatus s = PowerStatusReader.Read(out bool ok);
        if (!ok) { BattPercentText.Text = "--"; return; }

        int pct = s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent;
        bool charging = (s.BatteryFlag & 8) != 0;
        _onAc = s.ACLineStatus == 1;

        BattPercentText.Text = pct >= 0 ? pct + "%" : "--";

        int nearest = Math.Clamp(((pct + 5) / 10) * 10, 10, 100);
        if (pct >= 0 && pct < 10) nearest = 10;
        BatteryIcon.Level = nearest;
        BatteryIcon.IsCharging = charging;

        if (_onAc)
        {
            BattStatusText.Text = charging ? "正在充电" : "已接通电源";
        }
        else if (pct >= 0)
        {
            uint secs = s.BatteryLifeTime;
            BattStatusText.Text = (secs != 0xFFFFFFFF && secs > 0) ? "使用电池 · 剩余约 " + FormatDuration(secs) : "使用电池";
        }
        else
        {
            BattStatusText.Text = "电源状态未知";
        }

        UpdateModeUi();
        SyncChargeProtectUi();
    }

    // ---- charge protect ----

    private void ChargeProtectToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_adapter == null || _dryRun)
        {
            ChargeProtectToggle.IsChecked = _chargeProtect == true;
            return;
        }
        bool want = ChargeProtectToggle.IsChecked == true;
        if (want == _chargeProtect) { SyncChargeProtectUi(); return; }

        _chargeProtectSending = true;
        AppLog.Write("ChargeProtectToggle set mode=" + (want ? 1 : 0));
        _adapter.Execute($"{{\"method\":\"set_charging_protect\",\"params\":{{\"mode\":{(want ? 1 : 0)}}}}}");
        SyncChargeProtectUi();
    }

    private void Threshold_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string s || !int.TryParse(s, out int pct)) return;
        if (_adapter == null || _dryRun) return;

        int firmware = PercentToFirmware(pct);
        AppLog.Write("Threshold_Click pct=" + pct + " firmware=" + firmware);
        if (firmware <= 0) return;
        _chargeThreshold = firmware;
        SyncThresholdUi();
        _adapter.Execute($"{{\"method\":\"set_charging_threshold\",\"params\":{{\"mode\":{firmware}}}}}");
    }

    private void SyncChargeProtectToggle()
    {
        if (ChargeProtectToggle.IsChecked != _chargeProtect)
            ChargeProtectToggle.IsChecked = _chargeProtect;
        SyncChargeProtectUi();
    }

    private void SyncThresholdUi()
    {
        foreach (var (fw, pct) in ThresholdMap)
        {
            if (FindName("Thr" + pct) is Button b)
            {
                bool sel = fw == _chargeThreshold;
                b.Background = sel ? AccentBrush : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
                b.BorderBrush = sel ? AccentBrush : Brushes.Transparent;
            }
        }
        SyncChargeProtectUi();
    }

    private static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0xFF, 0x69, 0x00));

    private void SyncChargeProtectUi()
    {
        bool on = ChargeProtectToggle.IsChecked == true;
        bool charging = _chargingState == 1 || ((PowerStatusReader.Read().BatteryFlag & 8) != 0);
        ThresholdPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        int pct = FirmwareToPercent(_chargeThreshold);
        string limit = pct > 0 ? pct + "%" : "100%";
        if (on)
        {
            if (_onAc)
                ChargeProtectStatusText.Text = charging
                    ? $"充电中 · 达到 {limit} 后自动停止"
                    : $"电量已达 {limit}，已停止充电";
            else
                ChargeProtectStatusText.Text = $"充电保护已开启 · 电量达 {limit} 后停止充电";
        }
        else
        {
            ChargeProtectStatusText.Text = "未开启 · 电量将充至 100%";
        }
    }

    private void RefreshChargeProtect()
    {
        _adapter?.Execute("{\"method\":\"get_charging_protect\",\"params\":{}}");
        _adapter?.Execute("{\"method\":\"get_charging_threshold\",\"params\":{}}");
        _adapter?.Execute("{\"method\":\"get_charging_state\",\"params\":{}}");
    }

    private static string FormatDuration(uint seconds)
    {
        int h = (int)(seconds / 3600);
        int m = (int)((seconds % 3600) / 60);
        return h <= 0 ? m + " 分钟" : $"{h} 小时 {m} 分";
    }

    // ---- module events ----

    private void OnModuleSuccess(string method, string response, long qid)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                switch (method)
                {
                    case "get_workLoad_mode":
                    case "register_workLoad_mode_change":
                    {
                        int mode = ParseDataInt(response, "mode");
                        if (mode >= 0) _currentMode = ModeInfo.FromValue(mode);
                        break;
                    }
                    case "set_workLoad_mode":
                    {
                        int result = ParseDataInt(response, "result");
                        if (result != 0 && _pendingSet.HasValue)
                        {
                            _currentMode = _pendingSet.Value;
                            SetServiceStatus(true, "模式已切换：" + ModeInfo.GetName(_currentMode.Value));
                        }
                        else
                        {
                            _currentMode = null; // re-query below will restore authoritative value
                            _adapter?.Execute("{\"method\":\"get_workLoad_mode\",\"params\":{}}");
                        }
                        _pendingSet = null;
                        break;
                    }
                    case "get_workLoad_mode_decepticon_enable":
                    {
                        int enable = ParseDataInt(response, "enable");
                        if (enable >= 0) _standardCharger = enable == 1;
                        break;
                    }
                    case "register_ac_power_status":
                        _adapter?.Execute("{\"method\":\"get_workLoad_mode_decepticon_enable\",\"params\":{}}");
                        _adapter?.Execute("{\"method\":\"get_charging_protect\",\"params\":{}}");
                        _adapter?.Execute("{\"method\":\"get_charging_threshold\",\"params\":{}}");
                        _adapter?.Execute("{\"method\":\"get_charging_state\",\"params\":{}}");
                        break;
                    case "register_battery_notify":
                        ApplyBatteryNotify(response);
                        break;
                    case "get_charging_protect":
                    {
                        int mode = ParseDataInt(response, "mode");
                        if (mode >= 0)
                        {
                            _chargeProtect = mode == 1;
                            _chargeProtectSending = false;
                            SyncChargeProtectToggle();
                        }
                        break;
                    }
                    case "get_charging_threshold":
                    {
                        int mode = ParseDataInt(response, "mode");
                        if (mode >= 0)
                        {
                            _chargeThreshold = mode;
                            SyncThresholdUi();
                        }
                        break;
                    }
                    case "set_charging_protect":
                    {
                        int result = ParseDataInt(response, "result");
                        if (result != 0 && _chargeProtectSending)
                            _chargeProtect = ChargeProtectToggle.IsChecked == true;
                        _chargeProtectSending = false;
                        _adapter?.Execute("{\"method\":\"get_charging_protect\",\"params\":{}}");
                        break;
                    }
                    case "set_charging_threshold":
                    {
                        _adapter?.Execute("{\"method\":\"get_charging_threshold\",\"params\":{}}");
                        break;
                    }
                    case "get_charging_state":
                    {
                        int r = ParseDataInt(response, "result");
                        if (r >= 0)
                        {
                            _chargingState = r;
                            SyncChargeProtectUi();
                        }
                        break;
                    }
                    case "get_battery_info":
                        ApplyBatteryInfo(response);
                        break;
                }
                if (_currentMode.HasValue)
                    ModeNameText.Text = ModeInfo.GetName(_currentMode.Value);
                UpdateModeUi();
            }
            catch { }
        });
    }

    private void OnModuleFailure(string method, int code, string msg, long qid)
    {
        Dispatcher.BeginInvoke(() => SetServiceStatus(false, method + " 失败 (" + code + ")"));
    }

    private static int ParseDataInt(string json, string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty(key, out var v))
                return v.GetInt32();
        }
        catch { }
        return -1;
    }

    private int _batteryHealthPercent = -1;
    private int _batteryCycles = -1;

    private void ApplyBatteryInfo(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != System.Text.Json.JsonValueKind.Array ||
                data.GetArrayLength() == 0) return;
            var e = data[0];
            double design = e.TryGetProperty("design_capacity", out var d) ? d.GetDouble() : 0;
            double full = e.TryGetProperty("full_charged_capacity", out var f) ? f.GetDouble() : 0;
            int cycles = e.TryGetProperty("cycle_count", out var c) ? c.GetInt32() : -1;
            string model = e.TryGetProperty("model", out var m) ? m.GetString() : null;

            _batteryHealthPercent = (design > 0 && full > 0)
                ? Math.Min(100, (int)Math.Round(full / design * 100))
                : -1;
            _batteryCycles = cycles;
            UpdateBatteryHealthUi(model);
        }
        catch { }
    }

    private void UpdateBatteryHealthUi(string model)
    {
        string health = _batteryHealthPercent >= 0 ? _batteryHealthPercent + "%" : "--";
        string cycles = _batteryCycles >= 0 ? _batteryCycles + " 次" : "--";
        BattHealthText.Text = "健康度 " + health + " · 充电次数 " + cycles;
        BattHealthText.ToolTip = string.IsNullOrEmpty(model) ? null : "电池型号 " + model;
    }

    private void ApplyBatteryNotify(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            if (data.TryGetProperty("battery_percentage", out var bp))
            {
                int pct = bp.GetByte();
                _onAc = data.TryGetProperty("if_battery", out var ib) && ib.GetByte() == 0;
                bool charging = (PowerStatusReader.Read().BatteryFlag & 8) != 0;
                if (pct >= 0)
                {
                    BattPercentText.Text = pct + "%";
                    int nearest = Math.Clamp(((pct + 5) / 10) * 10, 10, 100);
                    if (pct < 10) nearest = 10;
                    BatteryIcon.Level = nearest;
                    BatteryIcon.IsCharging = charging;
                    BattStatusText.Text = _onAc ? (charging ? "正在充电" : "已接通电源") : "使用电池";
                    SyncChargeProtectUi();
                    UpdateModeUi();
                }
            }
        }
        catch { }
    }

    // ---- mode UI ----

    private void UpdateModeUi()
    {
        if (_dryRun) return;
        if (_currentMode.HasValue)
        {
            ModeNameText.Text = ModeInfo.GetName(_currentMode.Value);
            StateChip.Visibility = Visibility.Collapsed;
        }

        if (_onAc && _standardCharger)
        {
            ModeHintText.Text = "已连接电源适配器 · 静谧 / 智能 / 狂暴";
            Render(PerformanceMode.Quiet, PerformanceMode.Smart, PerformanceMode.Fierce);
        }
        else
        {
            ModeHintText.Text = "使用电池 · 省电 / 智能 / 极速";
            Render(PerformanceMode.PowerSave, PerformanceMode.Smart, PerformanceMode.Extreme);
        }
    }

    private void Render(PerformanceMode a, PerformanceMode b, PerformanceMode c)
    {
        _modes.Clear();
        foreach (var m in new[] { a, b, c })
            _modes.Add(new ModeItem(m, _currentMode == m));
    }

    private void ModeRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ModeItem item) return;

        if (_dryRun || _adapter == null)
        {
            MessageBox.Show(this, "未连接小米服务，无法切换性能模式。", "小米电源中心", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (item.Mode == _currentMode) return;

        int v = (int)item.Mode;
        _pendingSet = item.Mode;
        AppLog.Write("Click set_workLoad_mode mode=" + v);
        _adapter.Execute($"{{\"method\":\"set_workLoad_mode\",\"params\":{{\"mode\":{v}}}}}");
        SetServiceStatus(true, "正在切换为 " + ModeInfo.GetName(item.Mode) + " …");
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void FootLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int preference = 2;
        NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref preference, 4);
    }
}

public sealed class ModeItem
{
    public PerformanceMode Mode { get; }
    public string Name => ModeInfo.GetName(Mode);
    public string Subtitle => ModeInfo.GetSubtitle(Mode);
    public ImageSource Icon { get; }
    public bool IsSelected { get; }

    public ModeItem(PerformanceMode mode, bool selected)
    {
        Mode = mode;
        IsSelected = selected;
        Icon = ModeInfo.GetIconImage(mode);
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
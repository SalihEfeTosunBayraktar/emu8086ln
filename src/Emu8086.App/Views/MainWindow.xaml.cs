using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Emu8086.App.Controls;
using Emu8086.App.Services;
using Emu8086.App.ViewModels;
using Emu8086.Core.Devices;
using Microsoft.Win32;

namespace Emu8086.App.Views;

public partial class MainWindow : Window, IDialogService
{
    private const int RefreshIntervalMs = 33;
    private const int PortLogCapacity = 200;

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;
    private readonly List<Action> _deviceRefreshers = new();
    private readonly Dictionary<string, FrameworkElement> _deviceCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _portEvents = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _portLog = new();
    private TextBox? _printerBox;
    private readonly DockLayoutService _layout;
    private CpuVisualizerWindow? _visualizer;
    private ToolsWindow? _tools;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(this);
        DataContext = _vm;

        Screen.Session = _vm.Session;
        _vm.CaptureScreen = Screen.ToPng;
        Hex.Session = _vm.Session;
        BuildDevicePanel();

        _vm.InputRequested += OnInputRequested;
        _vm.DevicesRequested += ShowDevices;
        _vm.ProgramLoaded += OnProgramLoaded;
        _vm.Output.CollectionChanged += (_, _) =>
        {
            if (OutputList.Items.Count > 0) OutputList.ScrollIntoView(OutputList.Items[^1]);
        };
        _vm.Session.Machine.Ports.Accessed += OnPortAccessed;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs) };
        _timer.Tick += OnTick;

        _layout = new DockLayoutService(Dock);
        _layout.Restore();
        ApplyDockTheme();
        ThemeService.ThemeChanged += ApplyDockTheme;
        ApplyUiSettings();
        SettingsService.Changed += ApplyUiSettings;
        RestoreWindowPlacement();
        StateChanged += (_, _) => UpdateMaximizedLayout();
        VersionText.Text = "v" + UpdateService.CurrentVersion.ToString(3);
        Loaded += (_, _) =>
        {
            _vm.Startup(Environment.GetCommandLineArgs().Skip(1).ToArray());
            _timer.Start();
            if (SettingsService.Current.CheckForUpdates) _ = CheckForUpdatesAsync(silent: true);
        };
        Closing += OnClosing;
        Deactivated += (_, _) => _vm.OnApplicationDeactivated();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F12) return;
            OnVisualizerClick(this, new RoutedEventArgs());
            e.Handled = true;
        };
    }

    #region Lifetime

    private void OnTick(object? sender, EventArgs e)
    {
        _vm.Tick();
        Screen.Hint = _vm.State == SessionState.WaitingInput ? Loc.Instance["screen.hint"] : "";
        Screen.Refresh();
        if (Hex.IsVisible) Hex.Refresh();
        if (MemoryMap.IsVisible && _vm.State != SessionState.Empty) MemoryMap.Refresh(_vm.Session, _vm.StackTop, _vm.Variables);
        if (DevicesPanel.IsVisible)
        {
            foreach (var refresh in _deviceRefreshers) refresh();
            DrainPortLog();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_vm.ConfirmCloseAll())
        {
            e.Cancel = true;
            return;
        }
        _timer.Stop();
        SaveWindowPlacement();
        _layout.Save();
        _vm.Dispose();
    }

    private void RestoreWindowPlacement()
    {
        var s = SettingsService.Current;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        var s = SettingsService.Current;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
    }

    /// <summary>A maximized chrome-less window extends past the screen edge; compensate with a margin.</summary>
    private void UpdateMaximizedLayout()
    {
        bool max = WindowState == WindowState.Maximized;
        RootBorder.Margin = max ? new Thickness(7) : new Thickness(0);
        RootBorder.BorderThickness = max ? new Thickness(0) : new Thickness(1);
        MaximizeIcon.Data = (Geometry)FindResource(max ? "Icon.Restore" : "Icon.Maximize");
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    #endregion

    #region Emulator events

    private void OnInputRequested()
    {
        _layout.Panel("Screen").IsSelected = true;
        Screen.Focus();
    }

    private void OnProgramLoaded()
    {
        Screen.Invalidate();
        var cpu = _vm.Session.Machine.Cpu;
        Hex.Segment = cpu.DS;
        Hex.Offset = 0;
        MemoryAddress.Text = $"{cpu.DS:X4}:0000";
        _portLog.Clear();
        _printerBox?.Clear();
    }

    private void OnPortAccessed(int port, int value, bool isWrite, bool word)
    {
        string text = $"{(isWrite ? "OUT" : "IN ")}  {port,5}  ({port:X4}h)  {(word ? value.ToString("X4") : value.ToString("X2"))}h";
        _portEvents.Enqueue(text);
        while (_portEvents.Count > PortLogCapacity) _portEvents.TryDequeue(out _);
    }

    private void DrainPortLog()
    {
        while (_portEvents.TryDequeue(out var line))
        {
            _portLog.Insert(0, line);
            if (_portLog.Count > PortLogCapacity) _portLog.RemoveAt(_portLog.Count - 1);
        }
        if (_printerBox != null)
        {
            string text = _vm.Session.Machine.PrinterText;
            if (_printerBox.Text.Length != text.Length) _printerBox.Text = text;
        }
    }

    #endregion

    #region Devices

    private void BuildDevicePanel()
    {
        var ports = _vm.Session.Machine.Ports;
        AddDevice(new TrafficLightsView { Device = ports.Find<TrafficLights>() }, "traffic_lights", "device.trafficLights", "device.trafficLights.info");
        AddDevice(new StepperMotorView { Device = ports.Find<StepperMotor>() }, "stepper_motor", "device.stepper", "device.stepper.info");
        AddDevice(new LedDisplayView { Device = ports.Find<LedDisplay>() }, "led_display", "device.led", "device.led.info");
        AddDevice(new ThermometerView { Device = ports.Find<Thermometer>() }, "thermometer", "device.thermometer", "device.thermometer.info");
        AddDevice(new RobotView { Device = ports.Find<Robot>() }, "robot", "device.robot", "device.robot.info");

        _printerBox = new TextBox
        {
            IsReadOnly = true, Height = 120, Style = (Style)FindResource("MonoBox"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap,
        };
        AddCard(_printerBox, "printer", "device.printer", "device.printer.info");

        var portList = new ListBox { ItemsSource = _portLog, Height = 160, FontFamily = (FontFamily)FindResource("Font.Mono"), FontSize = 12 };
        AddCard(portList, "ports", "device.ports", "device.ports.info");
    }

    private void AddDevice<T>(DeviceView<T> view, string id, string titleKey, string infoKey) where T : DeviceBase
    {
        _deviceRefreshers.Add(view.Refresh);
        AddCard(view, id, titleKey, infoKey);
    }

    private void AddCard(FrameworkElement content, string id, string titleKey, string infoKey)
    {
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14 };
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"[{titleKey}]") { Source = Loc.Instance });
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 2, 0, 8) };
        info.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Secondary");
        info.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"[{infoKey}]") { Source = Loc.Instance });

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(info);
        stack.Children.Add(content);
        var card = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(8), Child = stack };
        card.SetResourceReference(Border.BackgroundProperty, "Bg.Panel2");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        card.BorderThickness = new Thickness(1);
        DevicesPanel.Children.Add(card);
        _deviceCards[id] = card;
    }

    private void ShowDevices(IReadOnlyList<string> devices)
    {
        var card = devices.Select(d => _deviceCards.GetValueOrDefault(d)).FirstOrDefault(c => c != null);
        if (card == null) return;
        _layout.Panel("Devices").IsSelected = true;
        Dispatcher.BeginInvoke(() => card.BringIntoView(), DispatcherPriority.Loaded);
    }

    #endregion

    #region Panels

    private void OnProjectFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectList.SelectedItem is ProjectFileItem item) _vm.OpenProjectFileCommand.Execute(item);
    }

    private void OnDiagnosticDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: DiagnosticItem d }) _vm.GoToDiagnosticCommand.Execute(d);
    }

    private void OnVariableDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: VariableRow row }) _vm.EditVariable(row);
    }

    private void OnRegisterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
        _vm.RefreshAll();
        e.Handled = true;
    }

    private void OnMemoryAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        GoToMemoryAddress();
        e.Handled = true;
    }

    private void OnMemoryGoClick(object sender, RoutedEventArgs e) => GoToMemoryAddress();

    private void GoToMemoryAddress()
    {
        string text = MemoryAddress.Text.Trim().TrimEnd('h', 'H');
        string[] parts = text.Split(':');
        bool ok = parts.Length switch
        {
            2 => int.TryParse(parts[0], NumberStyles.HexNumber, null, out int seg) & int.TryParse(parts[1], NumberStyles.HexNumber, null, out int off)
                 && SetMemoryView(seg, off),
            1 => int.TryParse(parts[0], NumberStyles.HexNumber, null, out int phys) && SetMemoryView(phys >> 4 & 0xF000, phys & 0xFFFF),
            _ => false,
        };
        if (!ok) MemoryAddress.SelectAll();
    }

    private bool SetMemoryView(int segment, int offset)
    {
        Hex.Segment = segment & 0xFFFF;
        Hex.Offset = offset & 0xFFFF;
        MemoryAddress.Text = $"{Hex.Segment:X4}:{offset & 0xFFFF:X4}";
        return true;
    }

    private void OnMemoryPresetClick(object sender, RoutedEventArgs e)
    {
        var cpu = _vm.Session.Machine.Cpu;
        _ = (sender as Button)?.Tag switch
        {
            "CS" => SetMemoryView(cpu.CS, cpu.IP),
            "DS" => SetMemoryView(cpu.DS, 0),
            "SS" => SetMemoryView(cpu.SS, cpu.SP),
            _ => SetMemoryView(0xB800, 0),
        };
    }

    /// <summary>Moves the emulator screen into its own window; it can be docked back by dragging.</summary>
    private void OnFloatScreenClick(object sender, RoutedEventArgs e) => _layout.Panel("Screen").Float();

    private void OnResetLayoutClick(object sender, RoutedEventArgs e) => _layout.Reset();

    /// <summary>Lists every panel with a check mark; unchecking hides it, checking shows it again.</summary>
    private void OnPanelsMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (MenuItem)sender;
        menu.Items.Clear();
        foreach (var panel in _layout.Panels)
        {
            if (panel.ContentId == "MemoryMap" && !SettingsService.Current.MemoryMap) continue;
            var item = new MenuItem { Header = panel.Title, IsCheckable = true, IsChecked = !panel.IsHidden };
            item.Click += (_, _) =>
            {
                if (panel.IsHidden) panel.Show();
                else panel.Hide();
            };
            menu.Items.Add(item);
        }
    }

    private void ApplyDockTheme() =>
        Dock.Theme = ThemeService.Current == ThemeService.Light ? new AvalonDock.Themes.Vs2013LightTheme() : new AvalonDock.Themes.Vs2013DarkTheme();

    private void ApplyUiSettings()
    {
        var s = SettingsService.Current;
        FontSize = s.UiFontSize;
        var memoryMap = _layout.Panel("MemoryMap");
        if (s.MemoryMap && memoryMap.IsHidden) memoryMap.Show();
        else if (!s.MemoryMap && !memoryMap.IsHidden) memoryMap.Hide();
        CompareMenu.Visibility = s.CompareRuns ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e) => _ = CheckForUpdatesAsync(silent: false);

    /// <summary>Checks GitHub for a newer release; when <paramref name="silent"/>, only reports a found update.</summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        var loc = Loc.Instance;
        UpdateInfo? update;
        try
        {
            update = await UpdateService.CheckAsync();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (!silent) MessageDialog.Show(this, loc.Format("update.failed", ex.Message));
            return;
        }

        if (update == null)
        {
            if (!silent) MessageDialog.Show(this, loc.Format("update.none", UpdateService.CurrentVersion.ToString(3)));
            return;
        }
        _vm.Log(loc.Format("update.found", update.Version.ToString(3)), OutputKind.Info);
        if (!UpdateDialog.Ask(this, update) || !_vm.ConfirmCloseAll()) return;

        try
        {
            var progress = new Progress<int>(p => VersionText.Text = loc.Format("update.downloading", p));
            if (await UpdateService.DownloadAndInstallAsync(update, progress))
            {
                _vm.Session.Stop();
                SettingsService.Save();
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException
                                       or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            VersionText.Text = "v" + UpdateService.CurrentVersion.ToString(3);
            MessageDialog.Show(this, loc.Format("update.failed", ex.Message));
        }
    }

    private void OnVisualizerClick(object sender, RoutedEventArgs e)
    {
        if (_visualizer != null)
        {
            _visualizer.Activate();
            return;
        }
        _visualizer = new CpuVisualizerWindow(this, _vm);
        _visualizer.Closed += (_, _) => _visualizer = null;
        _visualizer.Show();
    }

    private void OnConverterClick(object sender, RoutedEventArgs e) => ShowTools(ascii: false);
    private void OnAsciiClick(object sender, RoutedEventArgs e) => ShowTools(ascii: true);

    private void ShowTools(bool ascii)
    {
        if (_tools != null)
        {
            _tools.Activate(ascii);
            return;
        }
        _tools = new ToolsWindow(this);
        _tools.Closed += (_, _) => _tools = null;
        _tools.Show(ascii);
    }

    private CompareWindow? _compare;

    private void OnCompareClick(object sender, RoutedEventArgs e)
    {
        if (_compare != null)
        {
            _compare.Activate();
            return;
        }
        _compare = new CompareWindow(this, _vm, this);
        _compare.Closed += (_, _) => _compare = null;
        _compare.Show();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => new SettingsWindow(this, _vm).ShowDialog();

    private void OnReferenceClick(object sender, RoutedEventArgs e) => new ReferenceWindow { Owner = this }.Show();

    #endregion

    #region IDialogService

    public string? PickProjectFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{Loc.Instance["filter.project"]}|*{Project.Extension}",
            InitialDirectory = Directory.Exists(AppPaths.DefaultProjectsDirectory) ? AppPaths.DefaultProjectsDirectory : null,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? PickSourceFile()
    {
        var dialog = new OpenFileDialog { Filter = $"{Loc.Instance["filter.source"]}|*.asm;*.inc|{Loc.Instance["filter.all"]}|*.*" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? PickExecutableFile()
    {
        var dialog = new OpenFileDialog { Filter = $"{Loc.Instance["filter.executable"]}|*.com;*.exe;*.bin|{Loc.Instance["filter.all"]}|*.*" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string defaultName, string extension)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultName,
            DefaultExt = extension,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()}|*{extension}",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public NewProjectRequest? AskNewProject()
    {
        var dialog = new NewProjectDialog { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? AskText(string title, string prompt, string initial)
    {
        var dialog = new InputDialog(title, prompt, initial) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public ConfirmResult Confirm(string message) => MessageDialog.Ask(this, message);

    public void ShowMessage(string message) => MessageDialog.Show(this, message);

    public void ShowAbout() => new AboutWindow { Owner = this }.ShowDialog();

    #endregion
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Models;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>主界面可进入的功能页。</summary>
public enum PageKind
{
    Exam,
    Practice,
    WrongBook,
    Questions,
    Records,
}

/// <summary>
/// 主窗口的壳：顶栏（题库选择）、内容区（CurrentPage）、状态栏。
/// 页面一律做成 ViewModelBase，由 ViewLocator 按命名约定找对应 View。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IBankStore _store;
    private readonly AppSettingsStore _settingsStore;
    private readonly IDialogService? _dialogs;
    private readonly IExamRecordStore _recordStore;
    private readonly IBankProgressStore _progressStore;

    private AppSettings _settings = new();

    /// <summary>当前题库错题本里的题数，状态栏与首页卡片都要用。</summary>
    private int _selectedWrongCount;
    private bool _initialized;
    private bool _suspendSettingsSave;

    /// <summary>供 XAML 预览器使用。</summary>
    public MainViewModel()
        : this(new BankStore(), new AppSettingsStore(), null)
    {
    }

    public MainViewModel(
        IBankStore store,
        AppSettingsStore settingsStore,
        IDialogService? dialogs,
        IExamRecordStore? recordStore = null,
        IBankProgressStore? progressStore = null)
    {
        _store = store;
        _settingsStore = settingsStore;
        _dialogs = dialogs;
        _recordStore = recordStore ?? new ExamRecordStore();
        _progressStore = progressStore ?? new BankProgressStore();

        Home = new HomeViewModel(Navigate);
        CurrentPage = Home;
    }

    public HomeViewModel Home { get; }

    public ObservableCollection<BankSummary> Banks { get; } = [];

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    public partial BankSummary? SelectedBank { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    /// <summary>
    /// 正在答题（模拟考试/平时练习/错题本）时为 true：
    /// 主窗口收起顶栏和状态栏，页面上也不再显示「当前是什么模式」。
    /// </summary>
    public bool IsImmersive
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>状态栏右侧：当前题库、题量与错题数。</summary>
    public string BankStatusText => SelectedBank switch
    {
        null => "未选择题库",
        _ when _selectedWrongCount > 0
            => $"{SelectedBank.Name} · {SelectedBank.QuestionCount} 道题 · 错题 {_selectedWrongCount}",
        var bank => $"{bank.Name} · {bank.QuestionCount} 道题",
    };

    public bool StartMaximized { get; private set; }

    // ---------------------------------------------------------------- 渲染设置

    /// <summary>
    /// 是否启用硬件加速。写进 settings.json，下次启动生效。
    /// 与本次启动实际使用的模式不同时，菜单文字会提示需要重启。
    /// </summary>
    public bool HardwareAcceleration
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HardwareAccelerationMenuText));
            _ = ApplyRenderSettingAsync(value);
        }
    }

    /// <summary>本次启动的渲染方式由命令行指定，设置里的值只影响下次普通启动。</summary>
    public bool RenderModeOverriddenByCommandLine => Program.RenderModeFromCommandLine;

    /// <summary>本次启动实际用的渲染方式与设置不一致。</summary>
    public bool HasPendingRenderChange
        => !RenderModeOverriddenByCommandLine
            && HardwareAcceleration != Program.HardwareAccelerationEnabled;

    public string HardwareAccelerationMenuText
        => RenderModeOverriddenByCommandLine
            ? "启用硬件加速（本次由命令行指定）"
            : HasPendingRenderChange
                ? HardwareAcceleration ? "启用硬件加速（重启后生效）" : "软件渲染（重启后生效）"
                : "启用硬件加速";

    private async Task ApplyRenderSettingAsync(bool enabled)
    {
        // 启动时从设置里回填不加提示，也不重复写盘。
        if (!_initialized || _suspendSettingsSave)
        {
            return;
        }

        try
        {
            _settings.HardwareAcceleration = enabled;
            await _settingsStore.SaveAsync();

            if (RenderModeOverriddenByCommandLine)
            {
                // 命令行定下了本次运行的渲染方式，再重启也还是它，不打扰用户。
                StatusText = "渲染设置已保存，重启后按设置生效";
                return;
            }

            if (!HasPendingRenderChange)
            {
                StatusText = enabled ? "已启用硬件加速" : "已使用软件渲染";
                return;
            }

            if (_dialogs is null)
            {
                StatusText = "渲染设置已保存，重启后生效";
                return;
            }

            var restart = await _dialogs.ConfirmAsync(
                "需要重启",
                enabled
                    ? "硬件加速在程序启动时才会启用，重启后生效。现在重启吗？"
                    : "软件渲染在程序启动时才会切换，重启后生效。现在重启吗？",
                "立即重启");

            if (!restart)
            {
                StatusText = "渲染设置已保存，重启后生效";
                return;
            }

            if (!AppRestart.TryStartNewInstance())
            {
                StatusText = "无法自动重启，请手动关闭后重新打开";
                return;
            }

            // 先把窗口状态落盘，再直接退出：绕开关窗时的考试确认。
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                await SaveWindowStateAsync(window.WindowState == WindowState.Maximized);
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            // 切换流程里的弹窗、写盘、启动新实例都可能失败，落到状态栏而不是静默丢弃。
            StatusText = "渲染设置没有生效：" + ex.Message;
        }
    }

    // ---------------------------------------------------------------- 外观

    /// <summary>
    /// 界面深浅主题。切换后立即生效，并写进 settings.json。
    /// </summary>
    public ThemeMode Theme
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsThemeSystem));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeDark));
            _ = ApplyThemeAsync(value);
        }
    } = ThemeMode.System;

    public bool IsThemeSystem => Theme == ThemeMode.System;

    public bool IsThemeLight => Theme == ThemeMode.Light;

    public bool IsThemeDark => Theme == ThemeMode.Dark;

    private async Task ApplyThemeAsync(ThemeMode mode)
    {
        // 先换主题，让界面立刻响应；写盘可以慢一步。
        AppTheme.Apply(mode);

        // 启动时从设置里回填不重复写盘。
        if (!_initialized || _suspendSettingsSave)
        {
            return;
        }

        try
        {
            _settings.Theme = mode;
            await _settingsStore.SaveAsync();
        }
        catch (Exception ex)
        {
            StatusText = "主题设置没能保存：" + ex.Message;
        }
    }

    [RelayCommand]
    private void UseSystemTheme() => Theme = ThemeMode.System;

    [RelayCommand]
    private void UseLightTheme() => Theme = ThemeMode.Light;

    [RelayCommand]
    private void UseDarkTheme() => Theme = ThemeMode.Dark;

    // ---------------------------------------------------------------- 沉浸式作答

    partial void OnCurrentPageChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnPagePropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnPagePropertyChanged;
        }

        UpdateImmersive();
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 页面在 Running 与其它阶段之间切换时，外壳跟着收起或展开。
        if (e.PropertyName == "Phase")
        {
            UpdateImmersive();
        }
    }

    private void UpdateImmersive()
        => IsImmersive = CurrentPage is IAnsweringSession { IsAnswering: true };

    // ---------------------------------------------------------------- 启动 / 持久化

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        StartMaximized = _settings.IsWindowMaximized;

        // 回填开关当前的持久化值（这次运行实际用的模式是启动时定下的，可能不同）。
        _suspendSettingsSave = true;
        HardwareAcceleration = _settings.HardwareAcceleration;
        Theme = _settings.Theme;
        _suspendSettingsSave = false;

        await ReloadBanksAsync(_settings.LastBankId);
        _initialized = true;
        await RefreshRecordInfoAsync();

        // 首次启动（或上次选中的库已被删）时，把实际选中的库记下来。
        if (_settings.LastBankId != SelectedBank?.Id)
        {
            _settings.LastBankId = SelectedBank?.Id;
            await _settingsStore.SaveAsync();
        }
    }

    public async Task SaveWindowStateAsync(bool isMaximized)
    {
        _settings.IsWindowMaximized = isMaximized;
        await _settingsStore.SaveAsync();
    }

    private async Task ReloadBanksAsync(string? preferredId = null)
    {
        var target = preferredId ?? SelectedBank?.Id;

        try
        {
            var list = await _store.ListAsync();

            _suspendSettingsSave = true;
            Banks.Clear();
            foreach (var bank in list)
            {
                Banks.Add(bank);
            }

            SelectedBank = target is null
                ? Banks.FirstOrDefault()
                : Banks.FirstOrDefault(x => x.Id == target) ?? Banks.FirstOrDefault();
            _suspendSettingsSave = false;
            await RefreshSelectedProgressAsync();
        }
        catch (Exception ex)
        {
            _suspendSettingsSave = false;
            StatusText = "读取题库失败：" + ex.Message;
        }
    }

    /// <summary>读当前题库的错题本，更新状态栏与首页的错题数。</summary>
    private async Task RefreshSelectedProgressAsync()
    {
        var bankId = SelectedBank?.Id;
        var count = 0;

        if (bankId is not null)
        {
            try
            {
                var progress = await _progressStore.LoadAsync(bankId);
                // 题目被删掉后可能留下孤儿 Id，只算题库里还在的。
                var bank = await _store.LoadAsync(bankId);
                var alive = bank?.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal) ?? [];
                count = progress.WrongQuestionIds.Count(alive.Contains);
            }
            catch (Exception)
            {
                // 读不到进度就当作没有错题：状态栏不值得为此报错。
                count = 0;
            }
        }

        _selectedWrongCount = count;
        OnPropertyChanged(nameof(BankStatusText));
        Home.RefreshBankInfo(count);
    }

    /// <summary>把考试记录的条数与最近一次成绩同步到首页卡片。</summary>
    private async Task RefreshRecordInfoAsync()
    {
        try
        {
            var records = await _recordStore.ListAsync();
            var last = records.FirstOrDefault();
            Home.RefreshRecordInfo(records.Count, last?.Score, last?.Total);
        }
        catch (Exception)
        {
            Home.RefreshRecordInfo(0, null, null);
        }
    }

    // ---------------------------------------------------------------- 导航

    private void Navigate(PageKind kind)
    {
        CurrentPage = kind switch
        {
            PageKind.Exam => new ExamViewModel(
                _store,
                SelectedBank?.Id,
                SelectedBank?.Name,
                _dialogs,
                GoHome,
                _recordStore,
                _progressStore),
            PageKind.Practice => new PracticeViewModel(
                _store,
                SelectedBank?.Id,
                SelectedBank?.Name,
                GoHome,
                progress: _progressStore),
            PageKind.WrongBook => new PracticeViewModel(
                _store,
                SelectedBank?.Id,
                SelectedBank?.Name,
                GoHome,
                startWrongOnly: true,
                openPractice: () => Navigate(PageKind.Practice),
                progress: _progressStore),
            PageKind.Questions => new QuestionManageViewModel(
                _store,
                _dialogs,
                SelectedBank?.Id,
                SelectedBank?.Name,
                GoHome),
            PageKind.Records => new ExamRecordsViewModel(
                _recordStore,
                _dialogs,
                GoHome,
                () => Navigate(PageKind.Exam)),
            _ => Home,
        };

        StatusText = CurrentPage is PageViewModelBase page ? page.Title : "就绪";
    }

    [RelayCommand]
    private void GoHome()
    {
        CurrentPage = Home;
        StatusText = "就绪";

        // 从题目页回来时题量可能变了，顺便刷新顶栏和状态栏。
        _ = ReloadBanksAsync(SelectedBank?.Id);
        _ = RefreshRecordInfoAsync();
    }

    [RelayCommand]
    private async Task OpenBankManagerAsync()
    {
        if (_dialogs is null)
        {
            StatusText = "当前环境没有可用的窗口服务";
            return;
        }

        var manager = new BankManagerViewModel(_store, _dialogs, SelectedBank?.Id);
        await _dialogs.ShowBankManagerAsync(manager);

        await ReloadBanksAsync(manager.LastChangedBankId ?? SelectedBank?.Id);
    }

    [RelayCommand]
    private void About()
    {
        if (_dialogs is null)
        {
            StatusText = "当前环境没有可用的窗口服务";
            return;
        }

        _ = ShowAboutAsync(_dialogs);
    }

    private async Task ShowAboutAsync(IDialogService dialogs)
    {
        try
        {
            await dialogs.ShowAboutAsync();
        }
        catch (Exception ex)
        {
            StatusText = "无法打开关于窗口：" + ex.Message;
        }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // ---------------------------------------------------------------- 属性联动

    partial void OnSelectedBankChanged(BankSummary? value)
    {
        OnPropertyChanged(nameof(BankStatusText));
        _ = RefreshSelectedProgressAsync();

        if (!_initialized || _suspendSettingsSave)
        {
            return;
        }

        _settings.LastBankId = value?.Id;
        _ = _settingsStore.SaveAsync();
    }
}

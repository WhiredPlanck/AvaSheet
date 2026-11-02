using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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

    private AppSettings _settings = new();
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
        IExamRecordStore? recordStore = null)
    {
        _store = store;
        _settingsStore = settingsStore;
        _dialogs = dialogs;
        _recordStore = recordStore ?? new ExamRecordStore();

        Home = new HomeViewModel(Navigate, () => SelectedBank);
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

    private bool _isImmersive;

    /// <summary>
    /// 正在答题（模拟考试/平时练习/错题本）时为 true：
    /// 主窗口收起顶栏和状态栏，页面上也不再显示「当前是什么模式」。
    /// </summary>
    public bool IsImmersive
    {
        get => _isImmersive;
        private set => SetProperty(ref _isImmersive, value);
    }

    /// <summary>状态栏右侧：当前题库、题量与错题数。</summary>
    public string BankStatusText => SelectedBank switch
    {
        null => "未选择题库",
        { WrongCount: > 0 } bank => $"{bank.Name} · {bank.QuestionCount} 道题 · 错题 {bank.WrongCount}",
        var bank => $"{bank.Name} · {bank.QuestionCount} 道题",
    };

    public bool StartMaximized { get; private set; }

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
            Home.RefreshBankInfo();
        }
        catch (Exception ex)
        {
            _suspendSettingsSave = false;
            StatusText = "读取题库失败：" + ex.Message;
        }
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
                _recordStore),
            PageKind.Practice => new PracticeViewModel(
                _store,
                SelectedBank?.Id,
                SelectedBank?.Name,
                GoHome),
            PageKind.WrongBook => new PracticeViewModel(
                _store,
                SelectedBank?.Id,
                SelectedBank?.Name,
                GoHome,
                startWrongOnly: true,
                openPractice: () => Navigate(PageKind.Practice)),
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
    private void About() => StatusText = "ExamSystem 0.1 · 单机考试系统";

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
        Home.RefreshBankInfo();

        if (!_initialized || _suspendSettingsSave)
        {
            return;
        }

        _settings.LastBankId = value?.Id;
        _ = _settingsStore.SaveAsync();
    }
}

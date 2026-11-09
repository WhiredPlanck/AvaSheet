using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaSheet.Models;
using AvaSheet.Services;

namespace AvaSheet.ViewModels;

/// <summary>模拟考试当前处于哪个阶段。</summary>
public enum ExamPhase
{
    /// <summary>还没加载题库。</summary>
    Loading,

    /// <summary>没有题库，或题库里没有题目。</summary>
    Empty,

    /// <summary>组卷设置：看配比、改时长。</summary>
    Config,

    /// <summary>正在作答，可以前后翻页、随时改答案。</summary>
    Running,

    /// <summary>已交卷，卷面冻结、逐题显示对错与解析。</summary>
    Finished,
}

/// <summary>
/// 模拟考试：按题型配比随机组卷、限时作答、交卷后统一评分。
///
/// 和平时练习的区别是「先答完整张卷子再判」，所以过程中不显示对错，
/// 也可以前后翻页修改答案；多选题全对才得分，漏选错选都算 0 分。
/// </summary>
public sealed partial class ExamViewModel : PageViewModelBase, IAnsweringSession
{
    /// <summary>组卷配比：单选 : 多选 : 判断 = 2 : 1 : 1，题库不够时按实际数量抽。</summary>
    private const int TargetSingleChoice = 20;
    private const int TargetMultipleChoice = 10;
    private const int TargetTrueFalse = 10;

    private readonly IBankStore? _store;
    private readonly IExamRecordStore? _records;
    private readonly IBankProgressStore? _progressStore;
    private readonly string? _bankId;
    private readonly IDialogService? _dialogs;
    private readonly Action? _goBack;

    private QuestionBank? _bank;
    private DispatcherTimer? _timer;
    private TimeSpan _remaining;
    private DateTimeOffset _startedAt;
    private bool _submitted;

    /// <summary>用户是否亲手改过时长；没改过时，题量变了时长跟着推算值走。</summary>
    private bool _timeTouched;

    /// <summary>供 XAML 预览器使用。</summary>
    public ExamViewModel()
    {
        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
    }

    public ExamViewModel(
        IBankStore store,
        string? bankId,
        string? bankName,
        IDialogService? dialogs,
        Action? goBack = null,
        IExamRecordStore? records = null,
        IBankProgressStore? progress = null)
        : base(goBack)
    {
        _store = store;
        _bankId = bankId;
        BankName = bankName;
        _dialogs = dialogs;
        _goBack = goBack;
        _records = records;
        _progressStore = progress;

        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
    }

    public override string Title => "模拟考试";

    public override string Description => Phase switch
    {
        ExamPhase.Empty => "先去「管理题目」添加几道题。",
        ExamPhase.Config => "按题型组卷、限时作答，交卷后统一评分。",
        ExamPhase.Running => "可以前后翻页、随时改答案。",
        ExamPhase.Finished => "卷面已冻结，可以逐题翻看对错与解析。",
        _ => "按题型组卷、限时作答，交卷后统一评分。",
    };

    public string BankName => _bank?.Name ?? field ?? "未选择题库";

    [ObservableProperty]
    public partial ExamPhase Phase { get; set; } = ExamPhase.Loading;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>交卷后「本次错题已收进错题本」的提示，没收录时为 null。</summary>
    [ObservableProperty]
    public partial string? WrongBookNote { get; set; }

    public bool HasWrongBookNote => !string.IsNullOrEmpty(WrongBookNote);

    /// <summary>用户可改的限时（分钟）。默认按题量推算，可以自己填。</summary>
    [ObservableProperty]
    public partial int LimitMinutes { get; set; } = 1;

    /// <summary>限时输入框的值（NumericUpDown 绑 decimal）。</summary>
    public decimal? LimitMinutesValue
    {
        get => LimitMinutes;
        set
        {
            _timeTouched = true;
            LimitMinutes = Math.Clamp((int)Math.Round(value ?? 1), 1, 600);
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string RemainingText { get; set; } = "00:00";

    [ObservableProperty]
    public partial bool IsTimeLow { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsLoading => Phase == ExamPhase.Loading;

    public bool IsEmpty => Phase == ExamPhase.Empty;

    public bool IsConfig => Phase == ExamPhase.Config;

    public bool IsRunning => Phase == ExamPhase.Running;

    public bool IsFinished => Phase == ExamPhase.Finished;

    /// <summary>作答页与交卷后的回顾页共用一套布局。</summary>
    public bool ShowAnswerLayout => Phase is ExamPhase.Running or ExamPhase.Finished;

    /// <summary>作答与回顾时隐藏页面自己的大标题，只留细顶栏。</summary>
    public bool ShowPageHeader => !ShowAnswerLayout;

    /// <summary>作答中与交卷后的回顾都隐藏主窗口的顶栏和状态栏。</summary>
    public bool IsAnswering => ShowAnswerLayout;

    /// <summary>题库里各题型各有多少题（也是题量上限）。</summary>
    public int SingleAvailable { get; private set; }

    public int MultipleAvailable { get; private set; }

    public int TrueFalseAvailable { get; private set; }

    public string SingleLabel => $"单选题（现有 {SingleAvailable}）";

    public string MultipleLabel => $"多选题（现有 {MultipleAvailable}）";

    public string TrueFalseLabel => $"判断题（现有 {TrueFalseAvailable}）";

    /// <summary>题库里有没有这个题型；没有时勾选框也不该能勾。</summary>
    public bool SingleHasQuestions => SingleAvailable > 0;

    public bool MultipleHasQuestions => MultipleAvailable > 0;

    public bool TrueFalseHasQuestions => TrueFalseAvailable > 0;

    // ---- 组卷设置：各题型题量与分值 ----

    [ObservableProperty]
    public partial int SingleCount { get; set; }

    [ObservableProperty]
    public partial int MultipleCount { get; set; }

    [ObservableProperty]
    public partial int TrueFalseCount { get; set; }

    /// <summary>这种题型是否参与组卷；取消勾选等于把题量设为 0，勾回来时恢复原来的题量。</summary>
    [ObservableProperty]
    public partial bool SingleEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool MultipleEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool TrueFalseEnabled { get; set; } = true;

    private int _singleSaved;
    private int _multipleSaved;
    private int _trueFalseSaved;

    /// <summary>每种题型每题多少分，最小 0.5 分。</summary>
    [ObservableProperty]
    public partial double SingleScore { get; set; } = 1;

    [ObservableProperty]
    public partial double MultipleScore { get; set; } = 1;

    [ObservableProperty]
    public partial double TrueFalseScore { get; set; } = 1;

    public decimal? SingleCountValue
    {
        get => SingleCount;
        set
        {
            SingleCount = ClampCount(value, SingleAvailable);
            OnPropertyChanged();
        }
    }

    public decimal? MultipleCountValue
    {
        get => MultipleCount;
        set
        {
            MultipleCount = ClampCount(value, MultipleAvailable);
            OnPropertyChanged();
        }
    }

    public decimal? TrueFalseCountValue
    {
        get => TrueFalseCount;
        set
        {
            TrueFalseCount = ClampCount(value, TrueFalseAvailable);
            OnPropertyChanged();
        }
    }

    public decimal? SingleScoreValue
    {
        get => (decimal)SingleScore;
        set
        {
            SingleScore = ClampScore(value);
            OnPropertyChanged();
        }
    }

    public decimal? MultipleScoreValue
    {
        get => (decimal)MultipleScore;
        set
        {
            MultipleScore = ClampScore(value);
            OnPropertyChanged();
        }
    }

    public decimal? TrueFalseScoreValue
    {
        get => (decimal)TrueFalseScore;
        set
        {
            TrueFalseScore = ClampScore(value);
            OnPropertyChanged();
        }
    }

    private static int ClampCount(decimal? value, int max) =>
        Math.Clamp((int)Math.Round(value ?? 0, MidpointRounding.AwayFromZero), 0, Math.Max(0, max));

    private static double ClampScore(decimal? value)
    {
        var rounded = Math.Round((double)(value ?? 1) * 2, MidpointRounding.AwayFromZero) / 2;
        return Math.Clamp(rounded, 0.5, 100);
    }

    private static string FormatScore(double value) => value.ToString("0.##");

    /// <summary>勾回来时恢复的题量：优先上次手填的数，没有就用默认配比。</summary>
    private static int RestoreCount(int saved, int target, int available) =>
        Math.Clamp(saved > 0 ? saved : Math.Min(target, available), 0, Math.Max(0, available));

    partial void OnSingleCountChanged(int value)
    {
        SyncEnabled(value, SingleEnabled, v => SingleEnabled = v, v => _singleSaved = v);
        OnPaperChanged();
    }

    partial void OnMultipleCountChanged(int value)
    {
        SyncEnabled(value, MultipleEnabled, v => MultipleEnabled = v, v => _multipleSaved = v);
        OnPaperChanged();
    }

    partial void OnTrueFalseCountChanged(int value)
    {
        SyncEnabled(value, TrueFalseEnabled, v => TrueFalseEnabled = v, v => _trueFalseSaved = v);
        OnPaperChanged();
    }

    /// <summary>题量由手填改成 0 时自动取消勾选，填回正数时自动勾上。</summary>
    private static void SyncEnabled(int count, bool enabled, Action<bool> setEnabled, Action<int> save)
    {
        if (count > 0)
        {
            save(count);
            if (!enabled) setEnabled(true);
        }
        else if (enabled)
        {
            setEnabled(false);
        }
    }

    partial void OnSingleEnabledChanged(bool value)
    {
        if (value)
        {
            if (SingleCount == 0) SingleCount = RestoreCount(_singleSaved, TargetSingleChoice, SingleAvailable);
        }
        else if (SingleCount > 0)
        {
            _singleSaved = SingleCount;
            SingleCount = 0;
        }
    }

    partial void OnMultipleEnabledChanged(bool value)
    {
        if (value)
        {
            if (MultipleCount == 0) MultipleCount = RestoreCount(_multipleSaved, TargetMultipleChoice, MultipleAvailable);
        }
        else if (MultipleCount > 0)
        {
            _multipleSaved = MultipleCount;
            MultipleCount = 0;
        }
    }

    partial void OnTrueFalseEnabledChanged(bool value)
    {
        if (value)
        {
            if (TrueFalseCount == 0) TrueFalseCount = RestoreCount(_trueFalseSaved, TargetTrueFalse, TrueFalseAvailable);
        }
        else if (TrueFalseCount > 0)
        {
            _trueFalseSaved = TrueFalseCount;
            TrueFalseCount = 0;
        }
    }

    partial void OnSingleScoreChanged(double value) => OnPaperChanged();

    partial void OnMultipleScoreChanged(double value) => OnPaperChanged();

    partial void OnTrueFalseScoreChanged(double value) => OnPaperChanged();

    /// <summary>题量或分值变了：刷新摘要、满分，并按需跟着推算时长。</summary>
    private void OnPaperChanged()
    {
        if (!_timeTouched)
        {
            LimitMinutes = Math.Clamp(PaperSize, 1, 600);
            OnPropertyChanged(nameof(LimitMinutesValue));
        }

        OnPropertyChanged(nameof(PaperSize));
        OnPropertyChanged(nameof(PaperSizeText));
        OnPropertyChanged(nameof(FullScore));
        OnPropertyChanged(nameof(FullScoreText));
        OnPropertyChanged(nameof(PaperTotalsText));
        OnPropertyChanged(nameof(SingleLabel));
        OnPropertyChanged(nameof(MultipleLabel));
        OnPropertyChanged(nameof(TrueFalseLabel));
        StartCommand.NotifyCanExecuteChanged();
    }

    public int PaperSize => SingleCount + MultipleCount + TrueFalseCount;

    public string PaperSizeText => $"{PaperSize} 题";

    public double FullScore =>
        SingleCount * SingleScore + MultipleCount * MultipleScore + TrueFalseCount * TrueFalseScore;

    public string FullScoreText => $"满分 {FormatScore(FullScore)} 分";

    public string PaperTotalsText => PaperSize == 0
        ? "至少给一种题型留一道题。"
        : $"共 {PaperSize} 题 · 满分 {FormatScore(FullScore)} 分";

    public ObservableCollection<ExamQuestionViewModel> Questions { get; } = [];

    public ObservableCollection<ExamNavigatorItemViewModel> Navigator { get; } = [];

    [ObservableProperty]
    public partial int CurrentIndex { get; set; }

    public ExamQuestionViewModel? Current =>
        CurrentIndex >= 0 && CurrentIndex < Questions.Count ? Questions[CurrentIndex] : null;

    public string ProgressText =>
        Questions.Count == 0 ? "" : $"第 {CurrentIndex + 1} / {Questions.Count} 题";

    public double Progress =>
        Questions.Count == 0 ? 0 : (double)(CurrentIndex + 1) / Questions.Count;

    public int AnsweredCount => Questions.Count(q => q.IsAnswered);

    public string AnsweredText => $"已答 {AnsweredCount} / {Questions.Count}";

    public bool IsLastQuestion => CurrentIndex >= Questions.Count - 1;

    public bool IsFirstQuestion => CurrentIndex <= 0;

    // ---- 成绩 ----

    [ObservableProperty]
    public partial double Score { get; set; }

    public string ScoreText => FormatScore(Score);

    public string ScoreTotalText => $"/ {FormatScore(FullScore)}";

    public string AccuracyText =>
        FullScore <= 0 ? "0%" : $"{Math.Round(100.0 * Score / FullScore)}%";

    public string ScoreSummary
    {
        get
        {
            var unanswered = Questions.Count(q => !q.IsAnswered);
            var wrong = Questions.Count(q => q.IsAnswered) - Questions.Count(q => q.IsCorrect);
            return $"答对 {Questions.Count(q => q.IsCorrect)} 题 · 答错 {wrong} 题 · 未答 {unanswered} 题";
        }
    }

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "";

    public string UnansweredText { get; private set; } = "";

    public bool HasUnanswered { get; private set; }

    public IAsyncRelayCommand LeaveCommand { get; }

    /// <summary>供「关窗口」时询问用。</summary>
    public bool CanExitWithoutPrompt => Phase != ExamPhase.Running;

    /// <summary>组卷至少要有一道题。</summary>
    public bool CanStart => PaperSize > 0;

    // ---------------------------------------------------------------- 加载

    public async Task InitializeAsync()
    {
        if (Phase != ExamPhase.Loading)
        {
            return;
        }

        if (_store is null || string.IsNullOrEmpty(_bankId))
        {
            Phase = ExamPhase.Empty;
            NotifyAll();
            return;
        }

        try
        {
            var bank = await _store.LoadAsync(_bankId);
            if (bank is null)
            {
                Phase = ExamPhase.Empty;
                NotifyAll();
                return;
            }

            _bank = bank;
            PrepareConfig();
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorText.Describe(ex, "题库");
            Phase = ExamPhase.Empty;
            NotifyAll();
        }
    }

    private void PrepareConfig()
    {
        SingleAvailable = CountOf(QuestionType.SingleChoice);
        MultipleAvailable = CountOf(QuestionType.MultipleChoice);
        TrueFalseAvailable = CountOf(QuestionType.TrueFalse);

        // 重新组卷时回到默认配比与分值，时长也回到推算值。
        _timeTouched = false;
        SingleScore = 1;
        MultipleScore = 1;
        TrueFalseScore = 1;

        // 默认勾上题库里有题的题型；上次取消勾选的也复位。
        _singleSaved = 0;
        _multipleSaved = 0;
        _trueFalseSaved = 0;
        SingleEnabled = SingleAvailable > 0;
        MultipleEnabled = MultipleAvailable > 0;
        TrueFalseEnabled = TrueFalseAvailable > 0;

        SingleCount = Math.Min(TargetSingleChoice, SingleAvailable);
        MultipleCount = Math.Min(TargetMultipleChoice, MultipleAvailable);
        TrueFalseCount = Math.Min(TargetTrueFalse, TrueFalseAvailable);

        // 题量没变化时上面的 OnPaperChanged 不会触发，这里兜一次推算时长。
        LimitMinutes = Math.Clamp(PaperSize, 1, 600);

        Phase = PaperSize == 0 ? ExamPhase.Empty : ExamPhase.Config;

        NotifyAll();
    }

    private int CountOf(QuestionType type) =>
        _bank?.Questions.Count(q => q.Type == type) ?? 0;

    // ---------------------------------------------------------------- 组卷

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (_bank is null || PaperSize == 0)
        {
            return;
        }

        var questions = _bank.Questions;
        var paper = new List<Question>();

        paper.AddRange(Pick(questions, QuestionType.SingleChoice, SingleCount));
        paper.AddRange(Pick(questions, QuestionType.MultipleChoice, MultipleCount));
        paper.AddRange(Pick(questions, QuestionType.TrueFalse, TrueFalseCount));

        // 各题型内部已经打乱，这里再整体打乱一次，避免永远「单选在前」。
        paper = [.. paper.OrderBy(_ => Random.Shared.Next())];

        Questions.Clear();
        Navigator.Clear();

        for (var i = 0; i < paper.Count; i++)
        {
            var vm = new ExamQuestionViewModel(this, paper[i], i + 1);
            Questions.Add(vm);
            Navigator.Add(new ExamNavigatorItemViewModel(this, vm));
        }

        CurrentIndex = 0;
        Score = 0;
        _submitted = false;
        ErrorMessage = null;
        WrongBookNote = null;

        var minutes = Math.Clamp(LimitMinutes, 1, 600);
        _remaining = TimeSpan.FromMinutes(minutes);
        _startedAt = DateTimeOffset.Now;

        Phase = ExamPhase.Running;
        UpdateRemainingText();
        StartTimer();
        NotifyAll();
    }

    private static IEnumerable<Question> Pick(IReadOnlyList<Question> source, QuestionType type, int count) =>
        count <= 0
            ? []
            : source.Where(q => q.Type == type)
                .OrderBy(_ => Random.Shared.Next())
                .Take(count);

    // ---------------------------------------------------------------- 作答

    internal void OnAnswerChanged()
    {
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(AnsweredText));
        OnPropertyChanged(nameof(Progress));
        RefreshNavigator();
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentIndex < Questions.Count - 1)
        {
            GoTo(CurrentIndex + 1);
        }
    }

    [RelayCommand]
    private void Previous()
    {
        if (CurrentIndex > 0)
        {
            GoTo(CurrentIndex - 1);
        }
    }

    /// <summary>跳到第 index 题（从 0 开始）。</summary>
    public void GoTo(int index)
    {
        if (index < 0 || index >= Questions.Count || index == CurrentIndex)
        {
            RefreshNavigator();
            return;
        }

        CurrentIndex = index;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsFirstQuestion));
        OnPropertyChanged(nameof(IsLastQuestion));
        RefreshNavigator();
    }

    private void RefreshNavigator()
    {
        foreach (var item in Navigator)
        {
            item.Refresh();
        }
    }

    // ---------------------------------------------------------------- 计时

    private void StartTimer()
    {
        StopTimer();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    [RelayCommand]
    private void DecreaseMinutes()
    {
        LimitMinutesValue = LimitMinutes - 1;
    }

    [RelayCommand]
    private void IncreaseMinutes()
    {
        LimitMinutesValue = LimitMinutes + 1;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining -= TimeSpan.FromSeconds(1);

        if (_remaining <= TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
            UpdateRemainingText();
            _ = SubmitAsync(autoSubmitted: true);
            return;
        }

        UpdateRemainingText();
    }

    private void UpdateRemainingText()
    {
        var text = _remaining.TotalHours >= 1
            ? $"{(int)_remaining.TotalHours}:{_remaining.Minutes:00}:{_remaining.Seconds:00}"
            : $"{_remaining.Minutes:00}:{_remaining.Seconds:00}";

        RemainingText = text;
        IsTimeLow = _remaining <= TimeSpan.FromMinutes(5);
    }

    /// <summary>把时钟往前拨一段（给自动化测试用，正常运行由计时器驱动）。</summary>
    public void TickForTest(TimeSpan amount)
    {
        _remaining -= amount;
        if (_remaining <= TimeSpan.Zero)
        {
            _remaining = TimeSpan.Zero;
            UpdateRemainingText();
            _ = SubmitAsync(autoSubmitted: true);
            return;
        }

        UpdateRemainingText();
    }

    // ---------------------------------------------------------------- 交卷

    [RelayCommand]
    private async Task SubmitAsync()
    {
        await SubmitAsync(autoSubmitted: false);
    }

    private async Task SubmitAsync(bool autoSubmitted)
    {
        if (_submitted || Phase != ExamPhase.Running)
        {
            return;
        }

        var unanswered = Questions.Count(q => !q.IsAnswered);

        if (!autoSubmitted && _dialogs is not null)
        {
            var message = unanswered > 0
                ? $"还有 {unanswered} 题没有作答，确定要交卷吗？"
                : "确定要交卷吗？交卷后不能再修改答案。";

            if (!await _dialogs.ConfirmAsync(
                    unanswered > 0 ? "还有题目没答" : "确认交卷",
                    message,
                    "交卷"))
            {
                return;
            }
        }

        _submitted = true;
        StopTimer();

        foreach (var question in Questions)
        {
            question.Evaluate();
        }

        Score = Math.Round(Questions.Where(q => q.IsCorrect).Sum(q => q.Score), 2);
        UnansweredText = unanswered == 0
            ? "全部题目都已作答。"
            : $"有 {unanswered} 题未作答，按 0 分计。";
        HasUnanswered = unanswered > 0;

        var elapsed = DateTimeOffset.Now - _startedAt;
        ElapsedText = elapsed.TotalMinutes >= 1
            ? $"用时 {(int)elapsed.TotalMinutes} 分 {elapsed.Seconds} 秒"
            : $"用时 {Math.Max(1, (int)elapsed.TotalSeconds)} 秒";

        if (autoSubmitted)
        {
            ErrorMessage = "时间到，已自动交卷。";
        }
        else
        {
            ErrorMessage = null;
        }

        if (_records is not null)
        {
            try
            {
                await _records.SaveAsync(BuildRecord(autoSubmitted, elapsed));
            }
            catch (Exception ex)
            {
                // 记录写失败不影响看成绩，只在顶部提示一句。
                ErrorMessage = "成绩没能保存到考试记录：" + ex.Message;
            }
        }

        await CollectWrongQuestionsAsync();

        Phase = ExamPhase.Finished;
        NotifyAll();

        // 成绩单独弹一个小窗，关掉后回到冻结的卷面逐题回顾。
        await ShowResult();
    }

    /// <summary>把当前成绩整理成小窗用的快照。</summary>
    private ExamResultViewModel BuildResult() => new(
        BankName,
        ScoreText,
        ScoreTotalText,
        AccuracyText,
        ElapsedText,
        ScoreSummary,
        HasUnanswered ? UnansweredText : null,
        WrongBookNote);

    /// <summary>重新打开成绩小窗（回顾页侧栏的「查看成绩」）。</summary>
    [RelayCommand]
    private async Task ShowResult()
    {
        if (_dialogs is null || Phase != ExamPhase.Finished)
        {
            return;
        }

        await _dialogs.ShowExamResultAsync(BuildResult());
    }

    /// <summary>把这次答错的题收进错题本；错题本本来就有，就只提示一下。</summary>
    private async Task CollectWrongQuestionsAsync()
    {
        WrongBookNote = null;

        if (_progressStore is null || _bankId is null)
        {
            return;
        }

        var wrongIds = Questions
            .Where(q => q.IsAnswered && !q.IsCorrect)
            .Select(q => q.Question.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (wrongIds.Count == 0)
        {
            return;
        }

        try
        {
            var progress = await _progressStore.LoadAsync(_bankId);
            var existing = progress.WrongQuestionIds;
            var added = wrongIds
                .Where(id => !existing.Contains(id, StringComparer.Ordinal))
                .ToList();

            if (added.Count == 0)
            {
                WrongBookNote = $"本次答错的 {wrongIds.Count} 道题原本就在错题本里。";
                return;
            }

            progress.WrongQuestionIds = [.. existing, .. added];
            await _progressStore.SaveAsync(_bankId, progress);

            WrongBookNote = wrongIds.Count == added.Count
                ? $"本次答错的 {wrongIds.Count} 道题已加入错题本。"
                : $"本次答错的 {wrongIds.Count} 道题已加入错题本（其中 {added.Count} 道是新收录的）。";
        }
        catch (Exception ex)
        {
            // 错题本写失败同样不影响看成绩。
            var message = "错题没能写入错题本：" + ex.Message;
            ErrorMessage = string.IsNullOrEmpty(ErrorMessage) ? message : ErrorMessage + "　" + message;
        }
    }

    /// <summary>把这一份卷子的成绩与逐题作答整理成一条记录。</summary>
    private ExamRecord BuildRecord(bool autoSubmitted, TimeSpan elapsed)
    {
        return new ExamRecord
        {
            Id = "exam_" + Guid.NewGuid().ToString("N")[..12],
            BankId = _bankId,
            BankName = BankName,
            StartedAt = _startedAt,
            FinishedAt = DateTimeOffset.Now,
            ElapsedSeconds = Math.Max(0, (int)Math.Round(elapsed.TotalSeconds)),
            LimitMinutes = Math.Clamp(LimitMinutes, 1, 600),
            AutoSubmitted = autoSubmitted,
            Score = Score,
            Total = FullScore,
            CorrectCount = Questions.Count(q => q.IsCorrect),
            WrongCount = Questions.Count(q => q.IsAnswered && !q.IsCorrect),
            UnansweredCount = Questions.Count(q => !q.IsAnswered),
            Answers = [.. Questions.Select(q => new ExamRecordAnswer
            {
                QuestionId = q.Question.Id,
                Type = q.Question.Type,
                Stem = q.Question.Stem,
                Selected = [.. q.Options.Where(o => o.IsSelected).Select(o => o.Key)],
                Correct = [.. q.Options
                    .Where(o => q.Question.Answers.Contains(o.Key, StringComparer.OrdinalIgnoreCase))
                    .Select(o => o.Key)],
                IsAnswered = q.IsAnswered,
                IsCorrect = q.IsCorrect,
                Score = q.Score,
                Explanation = q.Question.Explanation,
            })],
        };
    }

    // ---------------------------------------------------------------- 退出

    /// <summary>
    /// 离开本页前的确认：考试进行中返回 false 表示不要走。
    /// 关窗口时也会调这个，保证「中途退出会作废」这件事只问一次。
    /// </summary>
    public async Task<bool> ConfirmExitAsync()
    {
        if (Phase != ExamPhase.Running)
        {
            return true;
        }

        if (_dialogs is null)
        {
            StopTimer();
            return true;
        }

        var ok = await _dialogs.ConfirmAsync(
            "退出考试",
            "退出后本次考试作废，已作答的内容不会保存。",
            "退出考试",
            destructive: true);

        if (ok)
        {
            StopTimer();
            Phase = ExamPhase.Config;
            NotifyAll();
        }

        return ok;
    }

    private async Task LeaveAsync()
    {
        if (!await ConfirmExitAsync())
        {
            return;
        }

        _goBack?.Invoke();
    }

    [RelayCommand]
    private void Restart()
    {
        _submitted = false;
        Score = 0;
        ElapsedText = "";
        ErrorMessage = null;
        WrongBookNote = null;
        Questions.Clear();
        Navigator.Clear();
        CurrentIndex = 0;

        // 保留用户上次设置的题量、分值与时长，直接回到组卷页。
        Phase = PaperSize == 0 ? ExamPhase.Empty : ExamPhase.Config;
        NotifyAll();
    }

    // ---------------------------------------------------------------- 通知

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(BankName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsConfig));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(ShowAnswerLayout));
        OnPropertyChanged(nameof(ShowPageHeader));
        OnPropertyChanged(nameof(IsAnswering));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(PaperTotalsText));
        OnPropertyChanged(nameof(SingleLabel));
        OnPropertyChanged(nameof(MultipleLabel));
        OnPropertyChanged(nameof(TrueFalseLabel));
        OnPropertyChanged(nameof(SingleAvailable));
        OnPropertyChanged(nameof(MultipleAvailable));
        OnPropertyChanged(nameof(TrueFalseAvailable));
        OnPropertyChanged(nameof(SingleHasQuestions));
        OnPropertyChanged(nameof(MultipleHasQuestions));
        OnPropertyChanged(nameof(TrueFalseHasQuestions));
        OnPropertyChanged(nameof(SingleCountValue));
        OnPropertyChanged(nameof(MultipleCountValue));
        OnPropertyChanged(nameof(TrueFalseCountValue));
        OnPropertyChanged(nameof(SingleScoreValue));
        OnPropertyChanged(nameof(MultipleScoreValue));
        OnPropertyChanged(nameof(TrueFalseScoreValue));
        OnPropertyChanged(nameof(LimitMinutesValue));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(FullScore));
        OnPropertyChanged(nameof(PaperSize));
        OnPropertyChanged(nameof(PaperSizeText));
        OnPropertyChanged(nameof(FullScoreText));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(AnsweredText));
        OnPropertyChanged(nameof(IsFirstQuestion));
        OnPropertyChanged(nameof(IsLastQuestion));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(ScoreTotalText));
        OnPropertyChanged(nameof(AccuracyText));
        OnPropertyChanged(nameof(ScoreSummary));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(UnansweredText));
        OnPropertyChanged(nameof(HasUnanswered));
        OnPropertyChanged(nameof(HasWrongBookNote));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(IsTimeLow));
        OnPropertyChanged(nameof(CanExitWithoutPrompt));
        RefreshNavigator();
    }
}

/// <summary>卷子上的一道题。</summary>
public sealed partial class ExamQuestionViewModel : ObservableObject
{
    private readonly ExamViewModel _owner;

    public ExamQuestionViewModel(ExamViewModel owner, Question question, int number)
    {
        _owner = owner;
        Question = question;
        Number = number;
        Score = question.Type switch
        {
            QuestionType.SingleChoice => owner.SingleScore,
            QuestionType.MultipleChoice => owner.MultipleScore,
            _ => owner.TrueFalseScore,
        };

        foreach (var choice in question.Choices)
        {
            Options.Add(new ExamOptionViewModel(this, choice.Key, choice.Text));
        }

        if (question.Type != QuestionType.TrueFalse) return;
        // 判断题没有 choices，用固定的两个选项。
        Options.Add(new ExamOptionViewModel(this, "T", "正确"));
        Options.Add(new ExamOptionViewModel(this, "F", "错误"));
    }

    public Question Question { get; }

    public int Number { get; }

    /// <summary>这道题在卷面上的分值。</summary>
    public double Score { get; }

    public string ScoreText => $"{Score:0.##} 分";

    public ObservableCollection<ExamOptionViewModel> Options { get; } = [];

    public bool IsMultiple => Question.Type == QuestionType.MultipleChoice;

    /// <summary>多选的提示只在作答时有用，判卷后收起。</summary>
    public bool ShowMultipleHint => IsMultiple && !IsRevealed;

    public string TypeShort => Question.Type.ToShortLabel();

    public string Stem => Question.Stem;

    public string DifficultyStars => new string('★', Math.Clamp(Question.Difficulty, 1, 5));

    public bool HasTags => Question.Tags.Count > 0;

    public string TagsText => string.Join(" · ", Question.Tags);

    public bool IsAnswered => Options.Any(o => o.IsSelected);

    public string CorrectAnswerText => AnswerText.Join(Options
        .Where(o => Question.Answers.Contains(o.Key, StringComparer.OrdinalIgnoreCase))
        .Select(o => o.Key));

    public bool IsCorrect { get; private set; }

    /// <summary>已经交卷并判定过，选项不再可改，正确答案也一并标出来。</summary>
    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    public string ResultTitle => !IsAnswered ? "未作答" : IsCorrect ? "答对了" : "答错了";

    public string ResultDetail => $"正确答案：{CorrectAnswerText}";

    public string ExplanationDisplay => "解析：" + ExplanationText;

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Question.Explanation);

    public string ExplanationText => Question.Explanation ?? "";

    internal void SelectOption(ExamOptionViewModel option)
    {
        if (IsRevealed)
        {
            return;
        }

        if (option.IsSelected)
        {
            // 单选/判断点已选项不再取消，避免误触把答案清空。
            if (!IsMultiple)
            {
                return;
            }

            option.IsSelected = false;
        }
        else
        {
            if (!IsMultiple)
            {
                foreach (var other in Options)
                {
                    other.SetSelectedQuiet(false);
                }
            }

            option.IsSelected = true;
        }

        OnPropertyChanged(nameof(IsAnswered));
        _owner.OnAnswerChanged();
    }

    internal void Evaluate()
    {
        var selected = Options.Where(o => o.IsSelected)
            .Select(o => o.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = Question.Answers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 多选全对才得分：漏选、多选、错选都算 0 分。
        IsCorrect = selected.SetEquals(expected);

        foreach (var option in Options)
        {
            option.SetRevealed(expected.Contains(option.Key));
        }

        IsRevealed = true;

        OnPropertyChanged(nameof(IsCorrect));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultDetail));
        OnPropertyChanged(nameof(ShowMultipleHint));
    }
}

/// <summary>卷子上的一个选项。</summary>
public sealed partial class ExamOptionViewModel(ExamQuestionViewModel owner, string key, string text) : ObservableObject
{
    public string Key { get; } = key;

    /// <summary>判断题用「正确 / 错误」，不显示 T / F 徽章。</summary>
    public bool HasKey => Key is not ("T" or "F");

    public string KeyText => Key;

    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsCorrectAnswer { get; set; }

    /// <summary>答错时把「本该选却没选」的项也标出来。</summary>
    public bool IsMissed => IsCorrectAnswer && !IsSelected;

    /// <summary>判卷前在已选项右侧打勾，判卷后改成标记正确答案。</summary>
    public bool ShowCheck => owner.IsRevealed ? IsCorrectAnswer : IsSelected;

    /// <summary>选了但选错的项。</summary>
    public bool IsWrongPick => owner.IsRevealed && IsSelected && !IsCorrectAnswer;

    /// <summary>判卷后选项不可再点。</summary>
    public bool IsActive => !owner.IsRevealed;

    public IRelayCommand SelectCommand => field ??= new RelayCommand(() => owner.SelectOption(this));

    internal void SetSelectedQuiet(bool value) => IsSelected = value;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheck));
        OnPropertyChanged(nameof(IsWrongPick));
        OnPropertyChanged(nameof(IsMissed));
    }

    internal void SetRevealed(bool correct)
    {
        IsCorrectAnswer = correct;
        OnPropertyChanged(nameof(IsMissed));
        OnPropertyChanged(nameof(IsWrongPick));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ShowCheck));
    }
}

/// <summary>右侧答题卡上的一个题号。</summary>
public sealed class ExamNavigatorItemViewModel(ExamViewModel owner, ExamQuestionViewModel question) : ObservableObject
{
    public int Number => question.Number;

    public bool IsCurrent => owner.CurrentIndex == question.Number - 1;

    public bool IsAnswered => question.IsAnswered;

    /// <summary>交卷后答题卡上按对错上色。</summary>
    public bool IsCorrect => question.IsRevealed && question.IsCorrect;

    public bool IsWrong => question.IsRevealed && question.IsAnswered && !question.IsCorrect;

    public bool IsSkipped => question.IsRevealed && !question.IsAnswered;

    public IRelayCommand JumpCommand => field ??= new RelayCommand(() => owner.GoTo(Number - 1));

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(IsAnswered));
        OnPropertyChanged(nameof(IsCorrect));
        OnPropertyChanged(nameof(IsWrong));
        OnPropertyChanged(nameof(IsSkipped));
    }
}

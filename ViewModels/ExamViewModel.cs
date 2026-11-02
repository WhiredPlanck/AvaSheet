using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Models;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

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

    /// <summary>已交卷，显示成绩与逐题回顾。</summary>
    Finished,
}

/// <summary>
/// 模拟考试：按题型配比随机组卷、限时作答、交卷后统一评分。
///
/// 和平时练习的区别是「先答完整张卷子再判」，所以过程中不显示对错，
/// 也可以前后翻页修改答案；多选题全对才得分，漏选错选都算 0 分。
/// </summary>
public sealed partial class ExamViewModel : PageViewModelBase
{
    /// <summary>组卷配比：单选 : 多选 : 判断 = 2 : 1 : 1，题库不够时按实际数量抽。</summary>
    private const int TargetSingleChoice = 20;
    private const int TargetMultipleChoice = 10;
    private const int TargetTrueFalse = 10;

    private readonly IBankStore? _store;
    private readonly IExamRecordStore? _records;
    private readonly string? _bankId;
    private readonly string? _initialBankName;
    private readonly IDialogService? _dialogs;
    private readonly Action? _goBack;

    private readonly List<ExamQuestionViewModel> _paper = [];
    private QuestionBank? _bank;
    private DispatcherTimer? _timer;
    private TimeSpan _remaining;
    private DateTimeOffset _startedAt;
    private bool _submitted;

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
        IExamRecordStore? records = null)
        : base(goBack)
    {
        _store = store;
        _bankId = bankId;
        _initialBankName = bankName;
        _dialogs = dialogs;
        _goBack = goBack;
        _records = records;

        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
    }

    public override string Title => "模拟考试";

    public override string Description => Phase switch
    {
        ExamPhase.Empty => "当前题库还没有题目，先去「管理题目」添加几道。",
        ExamPhase.Config => "按题型配比随机组卷、限时作答，交卷后统一评分。",
        ExamPhase.Running => "可以前后翻页、随时改答案；交卷前不显示对错。",
        ExamPhase.Finished => "成绩与逐题回顾都在下面。",
        _ => "按题型配比随机组卷、限时作答，交卷后统一评分。",
    };

    public string BankName => _bank?.Name ?? _initialBankName ?? "未选择题库";

    [ObservableProperty]
    public partial ExamPhase Phase { get; set; } = ExamPhase.Loading;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>用户可改的限时（分钟）。默认每题 1 分钟。</summary>
    [ObservableProperty]
    public partial int LimitMinutes { get; set; } = 1;

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

    /// <summary>题库里各题型各有多少题（设置页提示用）。</summary>
    public string BankSummaryText { get; private set; } = "";

    /// <summary>这一份卷子实际会抽多少题。</summary>
    public string PaperSummary { get; private set; } = "";

    public int SingleCount { get; private set; }

    public int MultipleCount { get; private set; }

    public int TrueFalseCount { get; private set; }

    public int PaperSize => SingleCount + MultipleCount + TrueFalseCount;

    public string PaperSizeText => $"{PaperSize} 题";

    public string FullScoreText => $"满分 {PaperSize} 分";

    public ObservableCollection<ExamQuestionViewModel> Questions { get; } = [];

    public ObservableCollection<ExamNavigatorItemViewModel> Navigator { get; } = [];

    /// <summary>成绩页的逐题回顾。</summary>
    public ObservableCollection<ExamQuestionViewModel> Review => Questions;

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
    public partial int Score { get; set; }

    public string ScoreText => $"{Score}";

    public string ScoreTotalText => $"/ {Questions.Count}";

    public string AccuracyText =>
        Questions.Count == 0 ? "0%" : $"{Math.Round(100.0 * Score / Questions.Count)}%";

    public string ScoreSummary
    {
        get
        {
            var unanswered = Questions.Count(q => !q.IsAnswered);
            var wrong = Questions.Count - Score - unanswered;
            return $"答对 {Score} 题 · 答错 {wrong} 题 · 未答 {unanswered} 题";
        }
    }

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "";

    public string UnansweredText { get; private set; } = "";

    public bool HasUnanswered { get; private set; }

    public IAsyncRelayCommand LeaveCommand { get; }

    /// <summary>供「关窗口」时询问用。</summary>
    public bool CanExitWithoutPrompt => Phase != ExamPhase.Running;

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
            ErrorMessage = Describe(ex);
            Phase = ExamPhase.Empty;
            NotifyAll();
        }
    }

    private void PrepareConfig()
    {
        SingleCount = Math.Min(TargetSingleChoice, CountOf(QuestionType.SingleChoice));
        MultipleCount = Math.Min(TargetMultipleChoice, CountOf(QuestionType.MultipleChoice));
        TrueFalseCount = Math.Min(TargetTrueFalse, CountOf(QuestionType.TrueFalse));

        BankSummaryText =
            $"题库现有：单选 {CountOf(QuestionType.SingleChoice)} · " +
            $"多选 {CountOf(QuestionType.MultipleChoice)} · " +
            $"判断 {CountOf(QuestionType.TrueFalse)}";

        PaperSummary = PaperSize == 0
            ? "题库里没有可抽取的题目。"
            : $"本次抽取：单选 {SingleCount} · 多选 {MultipleCount} · 判断 {TrueFalseCount}（共 {PaperSize} 题）";

        if (PaperSize == 0)
        {
            Phase = ExamPhase.Empty;
        }
        else
        {
            LimitMinutes = PaperSize;
            Phase = ExamPhase.Config;
        }

        NotifyAll();
    }

    private int CountOf(QuestionType type) =>
        _bank?.Questions?.Count(q => q.Type == type) ?? 0;

    // ---------------------------------------------------------------- 组卷

    [RelayCommand]
    private void Start()
    {
        if (_bank is null || PaperSize == 0)
        {
            return;
        }

        var questions = _bank.Questions ?? [];
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
        LimitMinutes = Math.Max(1, LimitMinutes - 5);
    }

    [RelayCommand]
    private void IncreaseMinutes()
    {
        LimitMinutes = Math.Min(600, LimitMinutes + 5);
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

        Score = Questions.Count(q => q.IsCorrect);
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

        Phase = ExamPhase.Finished;
        NotifyAll();
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
            Total = Questions.Count,
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
        Questions.Clear();
        Navigator.Clear();
        PrepareConfig();
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
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(BankSummaryText));
        OnPropertyChanged(nameof(PaperSummary));
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
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(IsTimeLow));
        OnPropertyChanged(nameof(CanExitWithoutPrompt));
        RefreshNavigator();
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "没有权限读取题库文件，检查一下数据目录的访问权限。",
        InvalidDataException => ex.Message,
        System.IO.FileNotFoundException => "题库文件不存在，可能已被移动或删除。",
        System.IO.IOException => $"读写题库失败：{ex.Message}",
        _ => $"出现意外错误：{ex.Message}",
    };
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

        foreach (var choice in question.Choices ?? [])
        {
            Options.Add(new ExamOptionViewModel(this, choice.Key, choice.Text));
        }

        if (question.Type == QuestionType.TrueFalse)
        {
            // 判断题没有 choices，用固定的两个选项。
            Options.Add(new ExamOptionViewModel(this, "T", "正确"));
            Options.Add(new ExamOptionViewModel(this, "F", "错误"));
        }
    }

    public Question Question { get; }

    public int Number { get; }

    public ObservableCollection<ExamOptionViewModel> Options { get; } = [];

    public bool IsMultiple => Question.Type == QuestionType.MultipleChoice;

    public string TypeShort => Question.Type.ToShortLabel();

    public string Stem => Question.Stem;

    public string DifficultyStars => new string('★', Math.Clamp(Question.Difficulty, 1, 5));

    public bool HasTags => (Question.Tags?.Count ?? 0) > 0;

    public string TagsText => string.Join(" · ", Question.Tags ?? []);

    public bool IsAnswered => Options.Any(o => o.IsSelected);

    public string AnswerKeysText => IsAnswered
        ? string.Join("、", Options.Where(o => o.IsSelected).Select(o => DisplayKey(o.Key)))
        : "未作答";

    public string CorrectAnswerText => string.Join("、",
        Options.Where(o => Question.Answers.Contains(o.Key, StringComparer.OrdinalIgnoreCase))
            .Select(o => DisplayKey(o.Key)));

    /// <summary>判断题对用户显示「正确 / 错误」，而不是 T / F。</summary>
    private static string DisplayKey(string key) => key switch
    {
        "T" => "正确",
        "F" => "错误",
        _ => key,
    };

    public bool IsCorrect { get; private set; }

    public string ResultText => !IsAnswered ? "未答" : IsCorrect ? "答对" : "答错";

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Question.Explanation);

    public string ExplanationText => Question.Explanation ?? "";

    internal void SelectOption(ExamOptionViewModel option)
    {
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
        OnPropertyChanged(nameof(AnswerKeysText));
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

        OnPropertyChanged(nameof(IsCorrect));
        OnPropertyChanged(nameof(ResultText));
    }
}

/// <summary>卷子上的一个选项。</summary>
public sealed partial class ExamOptionViewModel : ObservableObject
{
    private readonly ExamQuestionViewModel _owner;

    public ExamOptionViewModel(ExamQuestionViewModel owner, string key, string text)
    {
        _owner = owner;
        Key = key;
        Text = text;
    }

    public string Key { get; }

    /// <summary>判断题用「正确 / 错误」，不显示 T / F 徽章。</summary>
    public bool HasKey => Key is not ("T" or "F");

    public string KeyText => Key;

    public string Text { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsCorrectAnswer { get; set; }

    /// <summary>答错时把「本该选却没选」的项也标出来。</summary>
    public bool IsMissed => IsCorrectAnswer && !IsSelected;

    public IRelayCommand SelectCommand => _select ??= new RelayCommand(() => _owner.SelectOption(this));

    private IRelayCommand? _select;

    internal void SetSelectedQuiet(bool value) => IsSelected = value;

    internal void SetRevealed(bool correct)
    {
        IsCorrectAnswer = correct;
        OnPropertyChanged(nameof(IsMissed));
    }
}

/// <summary>右侧答题卡上的一个题号。</summary>
public sealed partial class ExamNavigatorItemViewModel : ObservableObject
{
    private readonly ExamViewModel _owner;
    private readonly ExamQuestionViewModel _question;

    public ExamNavigatorItemViewModel(ExamViewModel owner, ExamQuestionViewModel question)
    {
        _owner = owner;
        _question = question;
    }

    public int Number => _question.Number;

    public bool IsCurrent => _owner.CurrentIndex == _question.Number - 1;

    public bool IsAnswered => _question.IsAnswered;

    public IRelayCommand JumpCommand => _jump ??= new RelayCommand(() => _owner.GoTo(Number - 1));

    private IRelayCommand? _jump;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(IsAnswered));
    }
}

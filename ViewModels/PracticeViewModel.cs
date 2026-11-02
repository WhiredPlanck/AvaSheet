using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Models;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>练习页当前处于哪个阶段。</summary>
public enum PracticePhase
{
    /// <summary>还没加载题库。</summary>
    Loading,

    /// <summary>没有题库，或题库里没有题目。</summary>
    Empty,

    /// <summary>正在逐题作答。</summary>
    Running,

    /// <summary>一轮做完，显示成绩。</summary>
    Finished,
}

/// <summary>
/// 平时练习：把当前题库的题目随机打乱，逐题作答、即时判定、顺手记错题。
///
/// 单选题和判断题点一下选项就直接判；多选题选完再点「提交答案」。
/// 错题本存在题库文件的 wrongQuestionIds 里，答对后自动移出，
/// 所以它表示「当前还没掌握的题」，而不是历史错误次数。
/// </summary>
public sealed partial class PracticeViewModel : PageViewModelBase, IAnsweringSession
{
    private readonly IBankStore? _store;
    private readonly string? _bankId;
    private readonly string? _initialBankName;
    private readonly Action? _goBack;
    private readonly Action? _openPractice;
    private readonly bool _wrongBookEntry;

    private readonly HashSet<string> _wrongIds = new(StringComparer.Ordinal);
    private readonly List<Question> _queue = [];

    /// <summary>本轮每道题的判定结果：true = 答对。答题卡据此上色。</summary>
    private readonly Dictionary<string, bool> _results = new(StringComparer.Ordinal);

    private QuestionBank? _bank;
    private int _index;
    private DateTimeOffset _startedAt;
    private bool _initialized;
    private bool _wrongOnlyRound;
    private bool _dirty;

    /// <summary>供 XAML 预览器使用。</summary>
    public PracticeViewModel()
    {
        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
    }

    public PracticeViewModel(
        IBankStore store,
        string? bankId,
        string? bankName,
        Action? goBack = null,
        bool startWrongOnly = false,
        Action? openPractice = null)
        : base(goBack)
    {
        _store = store;
        _bankId = bankId;
        _initialBankName = bankName;
        _goBack = goBack;
        _wrongBookEntry = startWrongOnly;
        _openPractice = openPractice;

        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
    }

    public ObservableCollection<PracticeOptionViewModel> Options { get; } = [];

    /// <summary>右侧答题卡：本轮每道题的位置与对错，只用于展示，不能跳题。</summary>
    public ObservableCollection<PracticeNavItemViewModel> Navigator { get; } = [];

    public override string Title => _wrongBookEntry ? "错题本" : "平时练习";

    public override string Description => _wrongBookEntry
        ? Phase switch
        {
            PracticePhase.Empty => "错题本还没有题目，去平时练习做一轮，错的题会自动收进来。",
            PracticePhase.Finished => "这一轮错题做完了，答对的题已经移出错题本。",
            _ => "只练做错的题，答对后自动移出错题本。",
        }
        : Phase switch
        {
            PracticePhase.Empty => "当前题库还没有题目，先去「管理题目」添加几道。",
            PracticePhase.Finished => "这一轮做完了，看看哪些还没掌握。",
            _ => "逐题作答、即时判定，做错的题会自动进错题本。",
        };

    public string BankName => _bank?.Name ?? _initialBankName ?? "未选择题库";

    [ObservableProperty]
    public partial PracticePhase Phase { get; set; } = PracticePhase.Loading;

    [ObservableProperty]
    public partial int CorrectCount { get; set; }

    [ObservableProperty]
    public partial int WrongCount { get; set; }

    [ObservableProperty]
    public partial bool IsAnswered { get; set; }

    [ObservableProperty]
    public partial bool IsCorrectAnswer { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsLoading => Phase == PracticePhase.Loading;

    public bool IsEmpty => Phase == PracticePhase.Empty;

    public bool IsRunning => Phase == PracticePhase.Running;

    public bool IsFinished => Phase == PracticePhase.Finished;

    /// <summary>作答中隐藏主窗口的顶栏和状态栏。</summary>
    public bool IsAnswering => Phase == PracticePhase.Running;

    // ---------------------------------------------------------------- 当前题目

    public Question? Current => _index >= 0 && _index < _queue.Count ? _queue[_index] : null;

    public int Total => _queue.Count;

    public int AnsweredCount => CorrectCount + WrongCount;

    public double Progress => Total == 0 ? 0 : (double)AnsweredCount / Total;

    public string ProgressText => Total == 0 ? "" : $"第 {_index + 1} / {Total} 题";

    public string ScoreText => $"✓ {CorrectCount}　✗ {WrongCount}";

    public string Stem => Current?.Stem ?? "";

    public string TypeShort => Current?.Type.ToShortLabel() ?? "题目";

    public string DifficultyStars => Current is null
        ? ""
        : new string('★', Math.Clamp(Current.Difficulty, 1, 5));

    public bool HasTags => Current?.Tags.Count > 0;

    public string TagsText => Current is null ? "" : string.Join(" · ", Current.Tags);

    public bool IsMultiple => Current?.Type == QuestionType.MultipleChoice;

    /// <summary>多选题在提交前显示「提交答案」按钮。</summary>
    public bool ShowSubmit => IsMultiple && !IsAnswered;

    public bool CanSubmit => ShowSubmit && Options.Any(o => o.IsSelected);

    public bool HasResult => IsAnswered;

    public string ResultTitle => IsCorrectAnswer ? "答对了" : "答错了";

    public string ResultDetail
    {
        get
        {
            var answer = CorrectAnswerText;
            return $"正确答案：{answer}";
        }
    }

    public string CorrectAnswerText
    {
        get
        {
            if (Current is null)
            {
                return "";
            }

            if (Current.Type == QuestionType.TrueFalse)
            {
                return Current.Answers.FirstOrDefault() == "F" ? "错误" : "正确";
            }

            return string.Join("、", Current.Answers);
        }
    }

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Current?.Explanation);

    /// <summary>解析只在判定之后出现，避免提前剧透。</summary>
    public bool ShowExplanation => HasExplanation && IsAnswered;

    /// <summary>判定后跟结果放在同一个横条里，避免解析把「下一题」挤出视野。</summary>
    public string ExplanationDisplay => HasExplanation ? $"解析：{ExplanationText}" : "";

    public string ExplanationText => Current?.Explanation ?? "";

    public string NextButtonText => _index + 1 >= Total ? "查看结果" : "下一题";

    public string RoundModeText => _wrongOnlyRound ? "错题模式" : "随机顺序";

    /// <summary>本轮是不是只练错题。</summary>
    public bool IsWrongOnly => _wrongOnlyRound;

    // ---------------------------------------------------------------- 成绩

    public string AccuracyText => AnsweredCount == 0
        ? "0%"
        : $"{Math.Round(100.0 * CorrectCount / AnsweredCount)}%";

    public string ScoreSummary => $"答对 {CorrectCount}　答错 {WrongCount}　共 {AnsweredCount} 题";

    public string ElapsedText
    {
        get
        {
            var elapsed = DateTimeOffset.Now - _startedAt;
            var minutes = (int)elapsed.TotalMinutes;
            var seconds = elapsed.Seconds;
            return minutes > 0 ? $"用时 {minutes} 分 {seconds} 秒" : $"用时 {seconds} 秒";
        }
    }

    /// <summary>题库错题本里还没掌握的题目数。</summary>
    public int WrongBookCount => _wrongIds.Count;

    public bool HasWrongBook => WrongBookCount > 0;

    public string WrongBookText => WrongBookCount == 0
        ? "题库里没有错题，这一轮很干净。"
        : $"错题本里还有 {WrongBookCount} 道题，可以专门再刷一遍。";

    public string WrongOnlyButtonText => $"只练错题（{WrongBookCount}）";

    /// <summary>已经在错题模式里时，不再重复显示「只练错题」。</summary>
    public bool ShowWrongOnlyButton => HasWrongBook && !IsWrongOnly;

    public string RestartButtonText => _wrongBookEntry ? "再练错题" : "再练一轮";

    /// <summary>空状态文案随入口变化。</summary>
    public string EmptyTitle => _wrongBookEntry && _bank is not null
        ? "错题本还是空的"
        : "这里还没有可以练习的题目";

    public string EmptyHint => _wrongBookEntry && _bank is not null
        ? "平时练习里做错的题会自动收进来，答对之后又会自动移出。"
        : "先在顶栏选一个有题目的题库，或者去「管理题目」里添加几道。";

    /// <summary>错题本为空时给一个去平时练习的快捷入口。</summary>
    public bool ShowPracticeShortcut => _wrongBookEntry && _bank is not null;

    public IAsyncRelayCommand LeaveCommand { get; }

    [RelayCommand]
    private void OpenPractice() => _openPractice?.Invoke();

    // ---------------------------------------------------------------- 加载

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        if (_store is null || _bankId is null)
        {
            Phase = PracticePhase.Empty;
            NotifyAll();
            return;
        }

        try
        {
            _bank = await _store.LoadAsync(_bankId);
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
            Phase = PracticePhase.Empty;
            NotifyAll();
            return;
        }

        if (_bank is null || _bank.Questions.Count == 0)
        {
            Phase = PracticePhase.Empty;
            NotifyAll();
            return;
        }

        _wrongIds.UnionWith(_bank.WrongQuestionIds);
        _wrongIds.RemoveWhere(id => _bank.Questions.All(q => q.Id != id));

        StartRound(wrongOnly: _wrongBookEntry);
    }

    /// <summary>开始一轮：题库全部题目，或者只练错题，顺序都随机打乱。</summary>
    private void StartRound(bool wrongOnly)
    {
        if (_bank is null)
        {
            return;
        }

        _queue.Clear();
        var source = wrongOnly
            ? _bank.Questions.Where(q => _wrongIds.Contains(q.Id))
            : _bank.Questions;

        _queue.AddRange(source.OrderBy(_ => Random.Shared.Next()));

        if (_queue.Count == 0)
        {
            // 从「错题本」卡片进来时，空状态面板本身已经说明情况，不用再报错。
            ErrorMessage = _wrongBookEntry ? null : "错题本是空的，先做一轮完整练习吧。";
            Phase = PracticePhase.Empty;
            NotifyAll();
            return;
        }

        _wrongOnlyRound = wrongOnly;
        _index = 0;
        CorrectCount = 0;
        WrongCount = 0;
        _results.Clear();
        BuildNavigator();
        _startedAt = DateTimeOffset.Now;
        Phase = PracticePhase.Running;

        LoadCurrent();
        NotifyAll();
    }

    private void BuildNavigator()
    {
        Navigator.Clear();

        for (var i = 0; i < _queue.Count; i++)
        {
            Navigator.Add(new PracticeNavItemViewModel(i + 1));
        }

        RefreshNavigator();
    }

    /// <summary>把当前位置和每题判定结果同步到答题卡。</summary>
    private void RefreshNavigator()
    {
        for (var i = 0; i < Navigator.Count && i < _queue.Count; i++)
        {
            var hasResult = _results.TryGetValue(_queue[i].Id, out var correct);
            Navigator[i].Refresh(i == _index, hasResult, hasResult && correct);
        }
    }

    private void LoadCurrent()
    {
        Options.Clear();
        IsAnswered = false;
        IsCorrectAnswer = false;
        ErrorMessage = null;

        if (Current is { } question)
        {
            if (question.Type == QuestionType.TrueFalse)
            {
                Options.Add(new PracticeOptionViewModel(this, "T", "正确"));
                Options.Add(new PracticeOptionViewModel(this, "F", "错误"));
            }
            else
            {
                foreach (var choice in question.Choices)
                {
                    Options.Add(new PracticeOptionViewModel(this, choice.Key, choice.Text));
                }
            }
        }

        RefreshNavigator();
        NotifyAll();
    }

    // ---------------------------------------------------------------- 作答

    /// <summary>点选项：单选题和判断题立即判定，多选题只切换选中状态。</summary>
    internal void SelectOption(PracticeOptionViewModel option)
    {
        if (!IsRunning || IsAnswered)
        {
            return;
        }

        if (!IsMultiple)
        {
            foreach (var item in Options)
            {
                item.IsSelected = ReferenceEquals(item, option);
            }

            Submit();
            return;
        }

        option.IsSelected = !option.IsSelected;
        ErrorMessage = null;
        OnPropertyChanged(nameof(CanSubmit));
    }

    [RelayCommand]
    private void SubmitAnswer()
    {
        if (!IsMultiple)
        {
            return;
        }

        Submit();
    }

    private void Submit()
    {
        if (IsAnswered || Current is not { } question)
        {
            return;
        }

        var chosen = Options
            .Where(o => o.IsSelected)
            .Select(o => o.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (chosen.Count == 0)
        {
            ErrorMessage = "先选择答案，再提交。";
            return;
        }

        var correct = question.Answers
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IsCorrectAnswer = chosen.SetEquals(correct);
        IsAnswered = true;
        ErrorMessage = null;

        foreach (var option in Options)
        {
            option.IsCorrect = correct.Contains(option.Key);
            option.RefreshState();
        }

        if (IsCorrectAnswer)
        {
            CorrectCount++;
            _wrongIds.Remove(question.Id);
        }
        else
        {
            WrongCount++;
            _wrongIds.Add(question.Id);
        }

        _results[question.Id] = IsCorrectAnswer;
        _dirty = true;
        RefreshNavigator();
        NotifyAll();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (!IsAnswered)
        {
            return;
        }

        await PersistAsync();

        if (_index + 1 >= _queue.Count)
        {
            Finish();
            return;
        }

        _index++;
        LoadCurrent();
    }

    [RelayCommand]
    private async Task FinishEarlyAsync()
    {
        await PersistAsync();
        Finish();
    }

    private void Finish()
    {
        Phase = PracticePhase.Finished;
        NotifyAll();
    }

    [RelayCommand]
    private void Restart() => StartRound(wrongOnly: _wrongBookEntry);

    [RelayCommand]
    private void PracticeWrongOnly()
    {
        if (_wrongIds.Count == 0)
        {
            ErrorMessage = "错题本是空的，先做一轮完整练习吧。";
            return;
        }

        StartRound(wrongOnly: true);
    }

    private async Task LeaveAsync()
    {
        await PersistAsync();
        _goBack?.Invoke();
    }

    // ---------------------------------------------------------------- 落盘

    private async Task PersistAsync()
    {
        if (_store is null || _bank is null || !_dirty)
        {
            return;
        }

        try
        {
            var alive = _bank.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
            _bank.WrongQuestionIds = [.. _wrongIds.Where(alive.Contains)];
            await _store.SaveAsync(_bank);
            _dirty = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
        }

        NotifyAll();
    }

    // ---------------------------------------------------------------- 通知

    partial void OnPhaseChanged(PracticePhase value) => NotifyAll();

    partial void OnCorrectCountChanged(int value) => NotifyStats();

    partial void OnWrongCountChanged(int value) => NotifyStats();

    partial void OnIsAnsweredChanged(bool value) => NotifyAll();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void NotifyStats()
    {
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(AccuracyText));
        OnPropertyChanged(nameof(ScoreSummary));
        OnPropertyChanged(nameof(WrongBookCount));
        OnPropertyChanged(nameof(HasWrongBook));
        OnPropertyChanged(nameof(WrongBookText));
        OnPropertyChanged(nameof(WrongOnlyButtonText));
        OnPropertyChanged(nameof(ShowWrongOnlyButton));
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsAnswering));
        OnPropertyChanged(nameof(BankName));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Stem));
        OnPropertyChanged(nameof(TypeShort));
        OnPropertyChanged(nameof(DifficultyStars));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(IsMultiple));
        OnPropertyChanged(nameof(ShowSubmit));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(ResultDetail));
        OnPropertyChanged(nameof(CorrectAnswerText));
        OnPropertyChanged(nameof(HasExplanation));
        OnPropertyChanged(nameof(ShowExplanation));
        OnPropertyChanged(nameof(ExplanationText));
        OnPropertyChanged(nameof(ExplanationDisplay));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(RoundModeText));
        OnPropertyChanged(nameof(IsWrongOnly));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(ShowPracticeShortcut));
        OnPropertyChanged(nameof(RestartButtonText));
        NotifyStats();
    }

    private static string Describe(Exception ex) => ex switch
    {
        InvalidDataException or FileNotFoundException => ex.Message,
        UnauthorizedAccessException => "没有权限读写数据目录，请检查当前用户对用户数据目录的访问权限。",
        IOException => "读写文件失败：" + ex.Message,
        _ => ex.Message,
    };
}

/// <summary>练习页里的一个选项。</summary>
public sealed partial class PracticeOptionViewModel : ObservableObject
{
    private readonly PracticeViewModel _owner;

    public PracticeOptionViewModel(PracticeViewModel owner, string key, string text)
    {
        _owner = owner;
        Key = key;
        Text = text;
        SelectCommand = new RelayCommand(() => _owner.SelectOption(this));
    }

    public string Key { get; }

    /// <summary>判断题不出 A/B，用对勾和叉表示。</summary>
    /// <summary>判断题没有 A/B 键位，界面上不显示键位徽章。</summary>
    public bool HasKey => Key is not ("T" or "F");

    public string KeyText => Key;

    public string Text { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>判定之后：这个选项是不是正确答案。</summary>
    [ObservableProperty]
    public partial bool IsCorrect { get; set; }

    public bool IsWrongPick => IsCorrect == false && IsSelected && _owner.IsAnswered;

    /// <summary>还没判定时，选中的选项右侧显示一个对勾（和考试页一致）。</summary>
    public bool ShowSelectionMark => IsSelected && !_owner.IsAnswered;

    public IRelayCommand SelectCommand { get; }

    internal void RefreshState()
    {
        OnPropertyChanged(nameof(IsWrongPick));
        OnPropertyChanged(nameof(ShowSelectionMark));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWrongPick));
        OnPropertyChanged(nameof(ShowSelectionMark));
    }
}

/// <summary>练习页答题卡上的一个号码，只显示状态，不能点击跳题。</summary>
public sealed partial class PracticeNavItemViewModel : ObservableObject
{
    public PracticeNavItemViewModel(int number)
    {
        Number = number;
    }

    public int Number { get; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsAnswered { get; set; }

    [ObservableProperty]
    public partial bool IsCorrect { get; set; }

    public bool IsWrong => IsAnswered && !IsCorrect;

    internal void Refresh(bool isCurrent, bool isAnswered, bool isCorrect)
    {
        IsCurrent = isCurrent;
        IsAnswered = isAnswered;
        IsCorrect = isCorrect;
        OnPropertyChanged(nameof(IsWrong));
    }
}

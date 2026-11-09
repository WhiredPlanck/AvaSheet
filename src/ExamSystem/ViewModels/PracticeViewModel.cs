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

    /// <summary>选题型、选顺序的起始页。</summary>
    Setup,

    /// <summary>正在逐题作答。</summary>
    Running,

    /// <summary>一轮做完，显示成绩。</summary>
    Finished,
}

/// <summary>
/// 平时练习：按题型一轮一轮地刷题，逐题作答、即时判定、顺手记错题。
///
/// 进入时先选题型和顺序（按题库顺序 / 随机），做过的题会记在
/// progress/{bankId}.json 的 practiceProgress 里，下一次接着做没做过的；
/// 清除进度或用「再练一轮」就从头开始。
///
/// 单选题和判断题点一下选项就直接判；多选题选完再点「提交答案」。
/// 错题本也在这个进度文件里，答对后自动移出，
/// 所以它表示「当前还没掌握的题」，而不是历史错误次数。
/// </summary>
public sealed partial class PracticeViewModel : PageViewModelBase, IAnsweringSession
{
    private readonly IBankStore? _store;
    private readonly IBankProgressStore? _progressStore;
    private readonly string? _bankId;
    private readonly Action? _goBack;
    private readonly Action? _openPractice;
    private readonly bool _wrongBookEntry;

    private readonly HashSet<string> _wrongIds = new(StringComparer.Ordinal);
    private readonly List<Question> _queue = [];

    /// <summary>每个题型已经做完的题目 Id，落盘到 progress/{bankId}.json 的 practiceProgress。</summary>
    private readonly Dictionary<QuestionType, HashSet<string>> _done = new()
    {
        [QuestionType.SingleChoice] = new(StringComparer.Ordinal),
        [QuestionType.MultipleChoice] = new(StringComparer.Ordinal),
        [QuestionType.TrueFalse] = new(StringComparer.Ordinal),
    };

    /// <summary>本轮每道题的判定结果：true = 答对。答题卡据此上色。</summary>
    private readonly Dictionary<string, bool> _results = new(StringComparer.Ordinal);

    private QuestionBank? _bank;
    private BankProgress _progress = new();
    private int _index;
    private DateTimeOffset _startedAt;
    private bool _initialized;
    private bool _wrongOnlyRound;
    private bool _dirty;

    /// <summary>本轮练的题型。</summary>
    private QuestionType _roundType = QuestionType.SingleChoice;

    /// <summary>本轮是不是按题库顺序（false 表示随机）。</summary>
    private bool _sequential;

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
        Action? openPractice = null,
        IBankProgressStore? progress = null)
        : base(goBack)
    {
        _store = store;
        _progressStore = progress;
        _bankId = bankId;
        BankName = bankName;
        _goBack = goBack;
        _wrongBookEntry = startWrongOnly;
        _openPractice = openPractice;

        LeaveCommand = new AsyncRelayCommand(LeaveAsync);
    }

    public ObservableCollection<PracticeOptionViewModel> Options { get; } = [];

    /// <summary>起始页的三个题型卡片。</summary>
    public ObservableCollection<PracticeTypeCardViewModel> TypeCards { get; } = [];

    /// <summary>右侧答题卡：本轮每道题的位置与对错，只用于展示，不能跳题。</summary>
    public ObservableCollection<PracticeNavItemViewModel> Navigator { get; } = [];

    public override string Title => _wrongBookEntry ? "错题本" : "平时练习";

    public override string Description => _wrongBookEntry
        ? Phase switch
        {
            PracticePhase.Empty => "去平时练习做一轮，做错的题会自动收进来。",
            PracticePhase.Finished => "这一轮错题做完了，答对的题已经移出错题本。",
            _ => "只练做错的题，答对后自动移出错题本。",
        }
        : Phase switch
        {
            PracticePhase.Empty => "先去「管理题目」添加几道题。",
            PracticePhase.Setup => "按题型练习，进度会自动记住。",
            PracticePhase.Finished => "这一轮做完了。",
            _ => "做错的题会自动收进错题本。",
        };

    public string BankName => _bank?.Name ?? field ?? "未选择题库";

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

    public bool IsSetup => Phase == PracticePhase.Setup;

    public bool IsRunning => Phase == PracticePhase.Running;

    public bool IsFinished => Phase == PracticePhase.Finished;

    /// <summary>作答中隐藏主窗口的顶栏和状态栏。</summary>
    public bool IsAnswering => Phase == PracticePhase.Running;

    // ---------------------------------------------------------------- 起始页

    public int TotalQuestions => _bank?.Questions.Count ?? 0;

    /// <summary>三个题型加起来已经做完的题数。</summary>
    public int TotalDone => _done.Values.Sum(set => set.Count);

    public bool HasAnyProgress => TotalDone > 0;

    public string SetupSummaryText => TotalQuestions == 0
        ? ""
        : $"已练习 {TotalDone} / {TotalQuestions} 题";

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

            return AnswerText.Join(Current.Answers);
        }
    }

    /// <summary>解析只在判定之后出现：解析文本放在结果条里，结果条本身只在判定后显示。</summary>
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Current?.Explanation);

    /// <summary>判定后跟结果放在同一个横条里，避免解析把「下一题」挤出视野。</summary>
    public string ExplanationDisplay => HasExplanation ? $"解析：{ExplanationText}" : "";

    public string ExplanationText => Current?.Explanation ?? "";

    public string NextButtonText => _index + 1 >= Total ? "查看结果" : "下一题";

    public string RoundModeText => _wrongOnlyRound
        ? "错题模式"
        : $"{_roundType.ToLabel()} · {(_sequential ? "按顺序" : "随机顺序")}";

    /// <summary>本轮是不是只练错题。</summary>
    public bool IsWrongOnly => _wrongOnlyRound;

    /// <summary>成绩页上说明这一题型的整体进度。</summary>
    public string FinishedProgressText
    {
        get
        {
            if (_wrongBookEntry)
            {
                return WrongBookCount == 0
                    ? "错题本已经清空了。"
                    : $"错题本里还有 {WrongBookCount} 道题。";
            }

            var total = _bank?.Questions.Count(q => q.Type == _roundType) ?? 0;
            var done = _done.TryGetValue(_roundType, out var set) ? set.Count : 0;
            return done >= total
                ? $"{_roundType.ToLabel()}已全部做完，再来一轮会从头开始。"
                : $"{_roundType.ToLabel()}已完成 {done} / {total} 题。";
        }
    }

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
            var seconds = Math.Max(1, (int)elapsed.TotalSeconds % 60);
            return minutes > 0 ? $"用时 {minutes} 分 {seconds} 秒" : $"用时 {seconds} 秒";
        }
    }

    /// <summary>题库错题本里还没掌握的题目数。</summary>
    public int WrongBookCount => _wrongIds.Count;

    public bool HasWrongBook => WrongBookCount > 0;

    public string WrongBookText => WrongBookCount == 0
        ? "题库里还没有错题。"
        : $"错题本里还有 {WrongBookCount} 道题，可以专门再刷一遍。";

    public string WrongOnlyButtonText => $"只练错题（{WrongBookCount}）";

    /// <summary>已经在错题模式里时，不再重复显示「只练错题」。</summary>
    public bool ShowWrongOnlyButton => HasWrongBook && !IsWrongOnly;

    public string RestartButtonText
    {
        get
        {
            if (_wrongBookEntry)
            {
                return "再练错题";
            }

            return IsRoundComplete ? "再练一轮" : "继续练习";
        }
    }

    /// <summary>这一题型里已经没有没做过的题了。</summary>
    private bool IsRoundComplete
    {
        get
        {
            var total = _bank?.Questions.Count(q => q.Type == _roundType) ?? 0;
            var done = _done.TryGetValue(_roundType, out var set) ? set.Count : 0;
            return total > 0 && done >= total;
        }
    }

    /// <summary>空状态文案随入口变化。</summary>
    public string EmptyTitle => _wrongBookEntry && _bank is not null
        ? "错题本还是空的"
        : "这里还没有可以练习的题目";

    public string EmptyHint => _wrongBookEntry && _bank is not null
        ? "去平时练习做一轮，做错的题会自动收进来。"
        : "先去「管理题目」添加几道题。";

    /// <summary>错题本为空时给一个去平时练习的快捷入口。</summary>
    public bool ShowPracticeShortcut => _wrongBookEntry && _bank is not null;

    public IAsyncRelayCommand LeaveCommand { get; }

    [RelayCommand]
    private void OpenPractice() => _openPractice?.Invoke();

    /// <summary>成绩页回到题型选择。</summary>
    [RelayCommand]
    private async Task SelectTypeAsync()
    {
        await PersistAsync();
        BuildTypeCards();
        Phase = PracticePhase.Setup;
        NotifyAll();
    }

    /// <summary>清空全部题型的练习进度。</summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (_bank is null || !HasAnyProgress)
        {
            return;
        }

        foreach (var set in _done.Values)
        {
            set.Clear();
        }

        _dirty = true;
        await PersistAsync();
        BuildTypeCards();
        NotifyAll();
    }

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
            ErrorMessage = ErrorText.Describe(ex);
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

        if (_progressStore is not null && _bankId is not null)
        {
            try
            {
                _progress = await _progressStore.LoadAsync(_bankId).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _progress = new BankProgress();
            }
        }

        _wrongIds.UnionWith(_progress.WrongQuestionIds);
        _wrongIds.RemoveWhere(id => _bank.Questions.All(q => q.Id != id));

        LoadProgress(_progress);

        if (_wrongBookEntry)
        {
            StartRound(wrongOnly: true);
            return;
        }

        BuildTypeCards();
        Phase = PracticePhase.Setup;
        NotifyAll();
    }

    /// <summary>把记下的练习进度读进来，顺手丢掉已经不存在或换了题型的 Id。</summary>
    private void LoadProgress(BankProgress progress)
    {
        foreach (var entry in progress.PracticeProgress)
        {
            if (_done.TryGetValue(entry.Type, out var set))
            {
                set.UnionWith(entry.DoneIds);
            }
        }

        var alive = (_bank?.Questions ?? [])
            .Select(q => (q.Id, q.Type))
            .ToHashSet();

        foreach (var (type, set) in _done)
        {
            set.RemoveWhere(id => !alive.Contains((id, type)));
        }
    }

    private void BuildTypeCards()
    {
        TypeCards.Clear();

        if (_bank is null)
        {
            return;
        }

        foreach (var type in new[] { QuestionType.SingleChoice, QuestionType.MultipleChoice, QuestionType.TrueFalse })
        {
            var total = _bank.Questions.Count(q => q.Type == type);
            TypeCards.Add(new PracticeTypeCardViewModel(this, type, total, _done[type].Count));
        }
    }

    /// <summary>从起始页进入某一题型的一轮练习。</summary>
    internal void StartType(QuestionType type, bool sequential)
    {
        if (_bank is null)
        {
            return;
        }

        _roundType = type;
        _sequential = sequential;
        StartRound(wrongOnly: false);
    }

    /// <summary>清掉一个题型的练习进度。</summary>
    internal async Task ClearTypeAsync(QuestionType type)
    {
        if (_bank is null || !_done.TryGetValue(type, out var set) || set.Count == 0)
        {
            return;
        }

        set.Clear();
        _dirty = true;
        await PersistAsync().ConfigureAwait(true);
        BuildTypeCards();
        NotifyAll();
    }

    /// <summary>开始一轮：某一题型里还没做过的题目，按顺序或者随机。</summary>
    private void StartRound(bool wrongOnly)
    {
        if (_bank is null)
        {
            return;
        }

        _queue.Clear();

        if (wrongOnly)
        {
            _queue.AddRange(_bank.Questions
                .Where(q => _wrongIds.Contains(q.Id))
                .OrderBy(_ => Random.Shared.Next()));
        }
        else
        {
            var done = _done[_roundType];
            var remaining = _bank.Questions
                .Where(q => q.Type == _roundType && !done.Contains(q.Id));

            _queue.AddRange(_sequential ? remaining : remaining.OrderBy(_ => Random.Shared.Next()));
        }

        if (_queue.Count == 0)
        {
            if (wrongOnly)
            {
                // 从「错题本」卡片进来时，空状态面板本身已经说明情况，不用再报错。
                ErrorMessage = _wrongBookEntry ? null : "错题本是空的，先做一轮完整练习吧。";
                Phase = PracticePhase.Empty;
            }
            else
            {
                ErrorMessage = $"{_roundType.ToLabel()}已经做完了，清除进度后再开始新一轮。";
                BuildTypeCards();
                Phase = PracticePhase.Setup;
            }

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

        // 错题模式不动练习进度，它只是临时刷一遍。
        if (!_wrongOnlyRound && _done.TryGetValue(_roundType, out var doneSet))
        {
            doneSet.Add(question.Id);
        }

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

    /// <summary>
    /// 再来一轮。这一题型全做完时清掉进度从头开始；还有没做的就继续做剩下的。
    /// 错题本则重新抽一遍当前的错题。
    /// </summary>
    [RelayCommand]
    private void Restart()
    {
        if (!_wrongBookEntry && IsRoundComplete && _done.TryGetValue(_roundType, out var done))
        {
            done.Clear();
            _dirty = true;
        }

        StartRound(wrongOnly: _wrongBookEntry);
    }

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
        if (_bank is null || _bankId is null || !_dirty)
        {
            return;
        }

        try
        {
            var alive = _bank.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
            _progress.WrongQuestionIds = [.. _wrongIds.Where(alive.Contains)];
            _progress.PracticeProgress =
            [
                .. _done
                    .Where(pair => pair.Value.Count > 0)
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new PracticeProgressEntry
                    {
                        Type = pair.Key,
                        DoneIds = [.. pair.Value.Where(alive.Contains)],
                    })
            ];

            if (_progressStore is not null)
            {
                await _progressStore.SaveAsync(_bankId, _progress);
            }

            _dirty = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorText.Describe(ex);
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
        OnPropertyChanged(nameof(IsSetup));
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
        OnPropertyChanged(nameof(FinishedProgressText));
        OnPropertyChanged(nameof(TotalQuestions));
        OnPropertyChanged(nameof(TotalDone));
        OnPropertyChanged(nameof(HasAnyProgress));
        OnPropertyChanged(nameof(SetupSummaryText));
        NotifyStats();
    }
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
public sealed partial class PracticeNavItemViewModel(int number) : ObservableObject
{
    public int Number { get; } = number;

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

/// <summary>练习起始页上的一张题型卡片。</summary>
public sealed class PracticeTypeCardViewModel : ObservableObject
{
    public PracticeTypeCardViewModel(PracticeViewModel owner, QuestionType type, int total, int done)
    {
        Type = type;
        Total = total;
        DoneCount = done;
        StartSequentialCommand = new RelayCommand(() => owner.StartType(Type, sequential: true));
        StartRandomCommand = new RelayCommand(() => owner.StartType(Type, sequential: false));
        ClearCommand = new AsyncRelayCommand(() => owner.ClearTypeAsync(Type));
    }

    public QuestionType Type { get; }

    public string TypeName => Type.ToLabel();

    public string TypeShort => Type.ToShortLabel();

    public int Total { get; }

    public int DoneCount { get; }

    public int Remaining => Math.Max(0, Total - DoneCount);

    public double Progress => Total == 0 ? 0 : (double)DoneCount / Total;

    public bool HasQuestions => Total > 0;

    public bool HasProgress => DoneCount > 0;

    /// <summary>这一题型已经没有没做过的题了。</summary>
    public bool IsComplete => Total > 0 && Remaining == 0;

    public bool ShowStartButtons => HasQuestions && !IsComplete;

    public bool ShowClearLink => HasProgress && !IsComplete;

    public string CountText => HasQuestions ? $"共 {Total} 题" : "题库里暂时没有这个题型";

    public string ProgressText => !HasQuestions
        ? ""
        : IsComplete
            ? "已全部完成"
            : DoneCount == 0
                ? "还没开始"
                : $"已完成 {DoneCount} / {Total}";

    public IRelayCommand StartSequentialCommand { get; }

    public IRelayCommand StartRandomCommand { get; }

    public IAsyncRelayCommand ClearCommand { get; }
}

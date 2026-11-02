using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Models;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>题目列表的题型筛选。</summary>
public enum QuestionFilterKind
{
    All,
    SingleChoice,
    MultipleChoice,
    TrueFalse,
}

/// <summary>
/// 题目管理页：列表 + 搜索 + 题型筛选 + 增删改 + 批量导入导出。
/// 题目是值对象，每次编辑都由 <see cref="QuestionEditorViewModel"/> 产出新实例，
/// 这里只负责替换、落盘和刷新列表。
/// </summary>
public sealed partial class QuestionManageViewModel : PageViewModelBase
{
    private readonly IBankStore? _store;
    private readonly IDialogService? _dialogs;
    private readonly string? _bankId;
    private readonly string? _initialBankName;

    private readonly List<Question> _all = [];
    private QuestionBank? _bank;
    private bool _initialized;

    /// <summary>供 XAML 预览器使用。</summary>
    public QuestionManageViewModel()
    {
    }

    public QuestionManageViewModel(
        IBankStore store,
        IDialogService? dialogs,
        string? bankId,
        string? bankName,
        Action? goBack = null)
        : base(goBack)
    {
        _store = store;
        _dialogs = dialogs;
        _bankId = bankId;
        _initialBankName = bankName;
    }

    public ObservableCollection<QuestionRowViewModel> Questions { get; } = [];

    public override string Title => "管理题目";

    public override string Description => _bank is null
        ? "选择一个题库后，就可以在这里维护里面的题目。"
        : StatsText;

    public string BankName => _bank?.Name ?? _initialBankName ?? "未选择题库";

    public bool HasBank => _bank is not null;

    public bool HasQuestions => Questions.Count > 0;

    /// <summary>题库本身是空的。</summary>
    public bool IsEmptyBank => _bank is not null && _all.Count == 0;

    /// <summary>有题目，但被搜索或筛选挡住了。</summary>
    public bool NoMatch => _bank is not null && _all.Count > 0 && Questions.Count == 0;

    public string StatsText
    {
        get
        {
            if (_all.Count == 0)
            {
                return "还没有题目，先新建一道吧。";
            }

            var single = _all.Count(q => q.Type == QuestionType.SingleChoice);
            var multiple = _all.Count(q => q.Type == QuestionType.MultipleChoice);
            var trueFalse = _all.Count(q => q.Type == QuestionType.TrueFalse);
            return $"共 {_all.Count} 道题 · 单选 {single} · 多选 {multiple} · 判断 {trueFalse}";
        }
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial QuestionFilterKind Filter { get; set; }

    public bool IsAllFilter => Filter == QuestionFilterKind.All;

    public bool IsSingleFilter => Filter == QuestionFilterKind.SingleChoice;

    public bool IsMultipleFilter => Filter == QuestionFilterKind.MultipleChoice;

    public bool IsTrueFalseFilter => Filter == QuestionFilterKind.TrueFalse;

    [ObservableProperty]
    public partial QuestionRowViewModel? SelectedQuestion { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // ---------------------------------------------------------------- 加载

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (_store is null || _bankId is null)
        {
            RefreshState();
            return;
        }

        try
        {
            _bank = await _store.LoadAsync(_bankId);
            _all.Clear();
            if (_bank is not null)
            {
                _all.AddRange(_bank.Questions);
            }

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
        }

        ApplyFilter();
        RefreshState();
    }

    // ---------------------------------------------------------------- 筛选

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFilterChanged(QuestionFilterKind value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsSingleFilter));
        OnPropertyChanged(nameof(IsMultipleFilter));
        OnPropertyChanged(nameof(IsTrueFalseFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private void FilterAll() => Filter = QuestionFilterKind.All;

    [RelayCommand]
    private void FilterSingle() => Filter = QuestionFilterKind.SingleChoice;

    [RelayCommand]
    private void FilterMultiple() => Filter = QuestionFilterKind.MultipleChoice;

    [RelayCommand]
    private void FilterTrueFalse() => Filter = QuestionFilterKind.TrueFalse;

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim() ?? "";

        IEnumerable<Question> query = _all;
        if (Filter != QuestionFilterKind.All)
        {
            var wanted = Filter switch
            {
                QuestionFilterKind.SingleChoice => QuestionType.SingleChoice,
                QuestionFilterKind.MultipleChoice => QuestionType.MultipleChoice,
                _ => QuestionType.TrueFalse,
            };
            query = query.Where(q => q.Type == wanted);
        }

        if (keyword.Length > 0)
        {
            query = query.Where(q => Matches(q, keyword));
        }

        Questions.Clear();
        var number = 1;
        foreach (var question in query)
        {
            Questions.Add(new QuestionRowViewModel(this, question, number++));
        }

        OnPropertyChanged(nameof(HasQuestions));
        OnPropertyChanged(nameof(NoMatch));
        OnPropertyChanged(nameof(IsEmptyBank));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(Description));
    }

    private static bool Matches(Question question, string keyword)
    {
        if (question.Stem.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (question.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return question.Choices.Any(c => c.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- 编辑

    [RelayCommand]
    private async Task AddQuestionAsync()
    {
        if (_dialogs is null || _bank is null)
        {
            ErrorMessage = "先在上方选择题库。";
            return;
        }

        var created = await _dialogs.EditQuestionAsync(null);
        if (created is null)
        {
            return;
        }

        _all.Add(created);
        await PersistAsync();
    }

    internal async Task EditAsync(QuestionRowViewModel row)
    {
        if (_dialogs is null || _bank is null)
        {
            return;
        }

        var edited = await _dialogs.EditQuestionAsync(row.Question);
        if (edited is null)
        {
            return;
        }

        var index = _all.FindIndex(q => q.Id == row.Question.Id);
        if (index >= 0)
        {
            _all[index] = edited;
        }
        else
        {
            _all.Add(edited);
        }

        await PersistAsync();
    }

    internal async Task DeleteAsync(QuestionRowViewModel row)
    {
        if (_dialogs is null || _bank is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "删除题目",
            $"确定要删除这道题吗？\n\n{TrimStem(row.Question.Stem)}",
            "删除",
            destructive: true);

        if (!confirmed)
        {
            return;
        }

        _all.RemoveAll(q => q.Id == row.Question.Id);
        await PersistAsync();
    }

    // ---------------------------------------------------------------- 批量导入导出

    [RelayCommand]
    private async Task ImportQuestionsAsync()
    {
        if (_dialogs is null || _bank is null)
        {
            ErrorMessage = "先在上方选择题库。";
            return;
        }

        var file = await _dialogs.PickImportFileAsync();
        if (file is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync();

            var imported = ParseQuestions(json);
            if (imported.Count == 0)
            {
                throw new InvalidDataException("文件里没有可导入的题目。");
            }

            var existing = new HashSet<string>(_all.Select(q => q.Id), StringComparer.Ordinal);
            foreach (var question in imported)
            {
                if (string.IsNullOrWhiteSpace(question.Id) || !existing.Add(question.Id))
                {
                    question.Id = NewQuestionId();
                    existing.Add(question.Id);
                }

                _all.Add(question);
            }

            await PersistAsync();
        });
    }

    [RelayCommand]
    private async Task ExportQuestionsAsync()
    {
        if (_dialogs is null || _bank is null)
        {
            ErrorMessage = "先在上方选择题库。";
            return;
        }

        if (_all.Count == 0)
        {
            ErrorMessage = "这个题库还没有题目可以导出。";
            return;
        }

        var file = await _dialogs.PickExportFileAsync(SafeFileName(_bank.Name) + "-题目");
        if (file is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            await JsonSerializer.SerializeAsync(stream, _all, JsonContext.Io);
            await stream.FlushAsync();
        });
    }

    private static List<Question> ParseQuestions(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("这不是有效的 JSON 文件。", ex);
        }

        JsonArray? array = root switch
        {
            JsonArray direct => direct,
            JsonObject obj when obj["questions"] is JsonArray nested => nested,
            _ => null,
        };

        if (array is null)
        {
            throw new InvalidDataException("文件里既不是题目数组，也没有 questions 字段。");
        }

        List<Question>? list;
        try
        {
            list = array.Deserialize<List<Question>>(JsonContext.Io);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("题目字段格式不正确。", ex);
        }

        if (list is null)
        {
            return [];
        }

        foreach (var question in list)
        {
            question.Choices ??= [];
            question.Answers ??= [];
            question.Tags ??= [];
            question.Stem = question.Stem?.Trim() ?? "";
            question.Difficulty = Math.Clamp(question.Difficulty, 1, 5);
        }

        return list.Where(q => q.Stem.Length > 0).ToList();
    }

    // ---------------------------------------------------------------- 落盘

    private async Task PersistAsync()
    {
        if (_store is null || _bank is null)
        {
            return;
        }

        try
        {
            _bank.Questions = [.. _all];
            _bank.UpdatedAt = DateTimeOffset.Now;
            await _store.SaveAsync(_bank);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
        }

        ApplyFilter();
        RefreshState();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(HasBank));
        OnPropertyChanged(nameof(BankName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private static string TrimStem(string stem)
    {
        var text = stem.Trim();
        return text.Length <= 60 ? text : text[..60] + "…";
    }

    private static string NewQuestionId() => "q_" + Guid.NewGuid().ToString("N")[..12];

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = name.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalid, buffer[i]) >= 0)
            {
                buffer[i] = '_';
            }
        }

        var safe = new string(buffer).Trim();
        return string.IsNullOrEmpty(safe) ? "题库" : safe;
    }

    private static string Describe(Exception ex) => ex switch
    {
        InvalidDataException or FileNotFoundException => ex.Message,
        UnauthorizedAccessException => "没有权限读写数据目录，请检查当前用户对用户数据目录的访问权限。",
        IOException => "读写文件失败：" + ex.Message,
        _ => ex.Message,
    };
}

/// <summary>题目列表里的一行。</summary>
public sealed partial class QuestionRowViewModel : ObservableObject
{
    private readonly QuestionManageViewModel _owner;

    public QuestionRowViewModel(QuestionManageViewModel owner, Question question, int number)
    {
        _owner = owner;
        Question = question;
        Number = number;
        EditCommand = new AsyncRelayCommand(() => _owner.EditAsync(this));
        DeleteCommand = new AsyncRelayCommand(() => _owner.DeleteAsync(this));
    }

    public Question Question { get; }

    public int Number { get; }

    public string TypeShort => Question.Type.ToShortLabel();

    public string TypeLabel => Question.Type.ToLabel();

    public string Stem => Question.Stem;

    public string AnswerText => Question.Type == QuestionType.TrueFalse
        ? (Question.Answers.FirstOrDefault() == "F" ? "答案：错误" : "答案：正确")
        : "答案：" + string.Join("、", Question.Answers);

    public string Footer => $"{AnswerText} · 难度 {new string('★', Math.Clamp(Question.Difficulty, 1, 5))}";

    public bool HasTags => Question.Tags.Count > 0;

    public string TagsText => string.Join(" · ", Question.Tags);

    public IAsyncRelayCommand EditCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}

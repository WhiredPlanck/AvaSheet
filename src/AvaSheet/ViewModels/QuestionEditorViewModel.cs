using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaSheet.Models;

namespace AvaSheet.ViewModels;

/// <summary>
/// 题目编辑器的 ViewModel。题目是值对象，这里始终编辑副本，
/// 点「保存」时 <see cref="TryBuild"/> 产出一份新的 <see cref="Question"/>。
/// </summary>
public sealed partial class QuestionEditorViewModel : ViewModelBase
{
    private const int MaxChoices = 8;

    private readonly Question? _original;

    /// <summary>构造期间不要做联动（单选互斥、刷新校验）。</summary>
    private bool _suspended;

    public QuestionEditorViewModel()
        : this(null)
    {
    }

    public QuestionEditorViewModel(Question? question)
    {
        _original = question;
        _suspended = true;

        if (question is null)
        {
            SelectedType = QuestionType.SingleChoice;
            AddChoiceCore();
            AddChoiceCore();
        }
        else
        {
            SelectedType = question.Type;
            Stem = question.Stem;
            Explanation = question.Explanation;
            Difficulty = Math.Clamp(question.Difficulty, 1, 5);
            TagsText = string.Join("、", question.Tags);

            if (question.Type == QuestionType.TrueFalse)
            {
                TrueAnswer = question.Answers.Count == 0
                             || !string.Equals(question.Answers[0], "F", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var answers = new HashSet<string>(question.Answers, StringComparer.OrdinalIgnoreCase);
                foreach (var choice in question.Choices)
                {
                    Choices.Add(new ChoiceRowViewModel(this)
                    {
                        Text = choice.Text,
                        IsCorrect = answers.Contains(choice.Key),
                    });
                }

                while (Choices.Count < 2)
                {
                    AddChoiceCore();
                }
            }
        }

        _suspended = false;
        RefreshKeys();
        RefreshState();
    }

    public ObservableCollection<ChoiceRowViewModel> Choices { get; } = [];

    public string Title => _original is null ? "新建题目" : "编辑题目";

    public bool IsSingleType => SelectedType == QuestionType.SingleChoice;

    public bool IsMultipleType => SelectedType == QuestionType.MultipleChoice;

    public bool IsChoiceType => SelectedType != QuestionType.TrueFalse;

    public bool IsTrueFalse => SelectedType == QuestionType.TrueFalse;

    public bool CanAddChoice => Choices.Count < MaxChoices;

    public string DifficultyText => new string('★', Difficulty) + new string('☆', 5 - Difficulty);

    /// <summary>Slider 用 double；转成整数再存，避免绑定层的类型转换歧义。</summary>
    public double DifficultyValue
    {
        get => Difficulty;
        set => Difficulty = (int)Math.Round(value);
    }

    public bool IsTrueAnswer
    {
        get => TrueAnswer;
        set
        {
            if (value)
            {
                TrueAnswer = true;
            }
        }
    }

    public bool IsFalseAnswer
    {
        get => !TrueAnswer;
        set
        {
            if (value)
            {
                TrueAnswer = false;
            }
        }
    }

    [ObservableProperty]
    public partial QuestionType SelectedType { get; set; }

    [ObservableProperty]
    public partial string Stem { get; set; } = "";

    [ObservableProperty]
    public partial string? Explanation { get; set; }

    [ObservableProperty]
    public partial int Difficulty { get; set; } = 2;

    [ObservableProperty]
    public partial string TagsText { get; set; } = "";

    [ObservableProperty]
    public partial bool TrueAnswer { get; set; } = true;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // ---------------------------------------------------------------- 命令

    [RelayCommand]
    private void UseSingle() => SelectedType = QuestionType.SingleChoice;

    [RelayCommand]
    private void UseMultiple() => SelectedType = QuestionType.MultipleChoice;

    [RelayCommand]
    private void UseTrueFalse() => SelectedType = QuestionType.TrueFalse;

    [RelayCommand]
    private void AddChoice()
    {
        if (!CanAddChoice)
        {
            return;
        }

        AddChoiceCore();
        RefreshState();
    }

    // ---------------------------------------------------------------- 校验 / 产出

    public bool TryBuild(out Question? question, out string? error)
    {
        question = null;

        var stem = Stem.Trim();
        if (stem.Length == 0)
        {
            error = "题干不能为空。";
            return false;
        }

        var filled = Choices
            .Select((row, index) => (row, index))
            .Where(x => !string.IsNullOrWhiteSpace(x.row.Text))
            .ToList();

        var built = new Question
        {
            Id = _original?.Id ?? NewId(),
            Type = SelectedType,
            Stem = stem,
            Explanation = string.IsNullOrWhiteSpace(Explanation) ? null : Explanation.Trim(),
            Difficulty = Math.Clamp(Difficulty, 1, 5),
            Tags = ParseTags(TagsText),
        };

        if (SelectedType == QuestionType.TrueFalse)
        {
            built.Choices = [];
            built.Answers = [TrueAnswer ? "T" : "F"];
            question = built;
            error = null;
            return true;
        }

        if (filled.Count < 2)
        {
            error = "至少需要两个有内容的选项。";
            return false;
        }

        var correct = filled.Where(x => x.row.IsCorrect).ToList();
        if (SelectedType == QuestionType.SingleChoice && correct.Count != 1)
        {
            error = "单选题需要且只能有一个正确答案。";
            return false;
        }

        if (SelectedType == QuestionType.MultipleChoice && correct.Count < 2)
        {
            error = "多选题至少要有两个正确答案。";
            return false;
        }

        // Key 按最终保留的选项重新编号，删掉中间选项后不会出现 A、C 这种空档。
        for (var i = 0; i < filled.Count; i++)
        {
            filled[i].row.Key = KeyAt(i);
        }

        built.Choices =
        [
            .. filled
                .Select(x => new QuestionChoice { Key = x.row.Key, Text = x.row.Text.Trim() })
        ];
        built.Answers = [.. correct.Select(x => x.row.Key)];

        question = built;
        error = null;
        return true;
    }

    // ---------------------------------------------------------------- 联动

    partial void OnSelectedTypeChanged(QuestionType value)
    {
        OnPropertyChanged(nameof(IsSingleType));
        OnPropertyChanged(nameof(IsMultipleType));
        OnPropertyChanged(nameof(IsChoiceType));
        OnPropertyChanged(nameof(IsTrueFalse));

        // 构造期间由 ctor 自己负责填充选项，这里补空行会把已有选项顶到后面。
        if (!_suspended && value != QuestionType.TrueFalse)
        {
            while (Choices.Count < 2)
            {
                AddChoiceCore();
            }
        }

        RefreshState();
    }

    partial void OnStemChanged(string value) => RefreshState();

    partial void OnDifficultyChanged(int value)
    {
        OnPropertyChanged(nameof(DifficultyText));
        OnPropertyChanged(nameof(DifficultyValue));
        RefreshState();
    }

    partial void OnTrueAnswerChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTrueAnswer));
        OnPropertyChanged(nameof(IsFalseAnswer));
        RefreshState();
    }

    internal void OnChoiceCorrectChanged(ChoiceRowViewModel row, bool value)
    {
        if (_suspended)
        {
            return;
        }

        if (value && SelectedType == QuestionType.SingleChoice)
        {
            _suspended = true;
            foreach (var other in Choices)
            {
                if (!ReferenceEquals(other, row))
                {
                    other.IsCorrect = false;
                }
            }

            _suspended = false;
        }

        ErrorMessage = null;
    }

    internal void OnChoiceTextChanged() => RefreshState();

    internal void RemoveChoice(ChoiceRowViewModel row)
    {
        if (Choices.Count <= 2)
        {
            return;
        }

        Choices.Remove(row);
        RefreshKeys();
        RefreshState();
    }

    private void AddChoiceCore()
    {
        if (Choices.Count >= MaxChoices)
        {
            return;
        }

        Choices.Add(new ChoiceRowViewModel(this)
        {
            Key = KeyAt(Choices.Count),
            CanRemove = Choices.Count + 1 > 2,
        });
        RefreshKeys();
    }

    private void RefreshKeys()
    {
        var canRemove = Choices.Count > 2;
        for (var i = 0; i < Choices.Count; i++)
        {
            Choices[i].Key = KeyAt(i);
            Choices[i].CanRemove = canRemove;
        }
    }

    private void RefreshState()
    {
        ErrorMessage = null;
        OnPropertyChanged(nameof(CanAddChoice));
    }

    private static string KeyAt(int index) => ((char)('A' + index)).ToString();

    private static List<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return
        [
            .. text
                .Split([',', '，', '、', ';', '；'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.CurrentCulture)
        ];
    }

    private static string NewId() => "q_" + Guid.NewGuid().ToString("N")[..12];
}

/// <summary>编辑器里的一行选项。</summary>
public sealed partial class ChoiceRowViewModel : ObservableObject
{
    private readonly QuestionEditorViewModel _owner;

    public ChoiceRowViewModel(QuestionEditorViewModel owner)
    {
        _owner = owner;
        RemoveCommand = new RelayCommand(() => _owner.RemoveChoice(this));
    }

    public IRelayCommand RemoveCommand { get; }

    [ObservableProperty]
    public partial string Key { get; set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool IsCorrect { get; set; }

    [ObservableProperty]
    public partial bool CanRemove { get; set; } = true;

    partial void OnIsCorrectChanged(bool value) => _owner.OnChoiceCorrectChanged(this, value);

    partial void OnTextChanged(string value) => _owner.OnChoiceTextChanged();
}

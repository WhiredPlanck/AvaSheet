using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>
/// 题库管理小窗：只管题库本身（新建 / 导入 / 导出 / 重命名 / 删除），不管里面的题目。
/// 所有 IO 都经 <see cref="IBankStore"/>，异常一律转成窗口内的提示，不吞掉。
/// </summary>
public sealed partial class BankManagerViewModel : ViewModelBase
{
    private readonly IBankStore _store;
    private readonly BankTransfer _transfer;
    private readonly IDialogService _dialogs;
    private readonly string? _initialBankId;

    public BankManagerViewModel(IBankStore store, IDialogService dialogs, string? currentBankId = null)
    {
        _store = store;
        _transfer = new BankTransfer(store);
        _dialogs = dialogs;
        _initialBankId = currentBankId;
    }

    public ObservableCollection<BankRowViewModel> Banks { get; } = [];

    /// <summary>窗口关闭后主界面据此决定选中哪个题库。</summary>
    public string? LastChangedBankId { get; private set; }

    public bool HasBanks => Banks.Count > 0;

    public string SummaryText
    {
        get
        {
            if (Banks.Count == 0)
            {
                return "还没有题库";
            }

            var total = 0;
            foreach (var row in Banks)
            {
                total += row.QuestionCount;
            }

            return $"共 {Banks.Count} 个题库 · {total} 道题";
        }
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? WarningMessage { get; set; }

    public Task InitializeAsync() => ReloadAsync();

    // ---------------------------------------------------------------- 操作

    [RelayCommand]
    private async Task CreateAsync()
    {
        var name = await _dialogs.PromptBankNameAsync("新建题库", "", "创建");
        if (name is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var id = await _store.CreateAsync(name);
            LastChangedBankId = id;
            await ReloadAsync();
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await _dialogs.PickImportFileAsync();
        if (file is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            var summary = await _transfer.ImportAsync(stream);
            LastChangedBankId = summary.Id;
            await ReloadAsync();
        });
    }

    internal async Task RenameAsync(BankRowViewModel row)
    {
        var name = await _dialogs.PromptBankNameAsync($"重命名「{row.Name}」", row.Name, "保存");
        if (name is null || string.Equals(name, row.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _store.RenameAsync(row.Id, name);
            row.Name = name.Trim();
            row.UpdatedAt = DateTimeOffset.Now;
            row.IsConfirmingDelete = false;
            OnPropertyChanged(nameof(SummaryText));
        });
    }

    internal async Task ExportAsync(BankRowViewModel row)
    {
        var file = await _dialogs.PickExportFileAsync(SafeFileName(row.Name));
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

            await _transfer.ExportAsync(row.Id, stream);
            ClearError();
        });
    }

    internal async Task DeleteAsync(BankRowViewModel row)
    {
        await RunAsync(async () =>
        {
            await _store.DeleteAsync(row.Id);
            if (string.Equals(LastChangedBankId, row.Id, StringComparison.Ordinal))
            {
                LastChangedBankId = null;
            }

            await ReloadAsync();
        });
    }

    // ---------------------------------------------------------------- 内部

    public async Task ReloadAsync()
    {
        try
        {
            var list = await _store.ListAsync();

            Banks.Clear();
            foreach (var summary in list)
            {
                Banks.Add(new BankRowViewModel(this, summary, summary.Id == _initialBankId));
            }

            OnPropertyChanged(nameof(HasBanks));
            OnPropertyChanged(nameof(SummaryText));

            WarningMessage = _store.UnreadableFiles.Count == 0
                ? null
                : $"有 {_store.UnreadableFiles.Count} 个文件无法解析，已跳过：{string.Join("、", _store.UnreadableFiles)}";
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearError();
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

    private void ClearError() => ErrorMessage = null;

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

/// <summary>题库列表里的一行。</summary>
public sealed partial class BankRowViewModel : ObservableObject
{
    private readonly BankManagerViewModel _owner;

    public BankRowViewModel(BankManagerViewModel owner, BankSummary summary, bool isCurrent)
    {
        _owner = owner;
        Id = summary.Id;
        Name = summary.Name;
        QuestionCount = summary.QuestionCount;
        UpdatedAt = summary.UpdatedAt;
        IsCurrent = isCurrent;
    }

    public string Id { get; }

    public bool IsCurrent { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meta))]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meta))]
    public partial int QuestionCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meta))]
    public partial DateTimeOffset UpdatedAt { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public string Meta
    {
        get
        {
            var count = QuestionCount == 0 ? "暂无题目" : $"{QuestionCount} 道题";
            return $"{count} · 更新于 {UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";
        }
    }

    [RelayCommand]
    private Task RenameAsync() => _owner.RenameAsync(this);

    [RelayCommand]
    private Task ExportAsync() => _owner.ExportAsync(this);

    [RelayCommand]
    private void RequestDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task ConfirmDeleteAsync() => _owner.DeleteAsync(this);
}

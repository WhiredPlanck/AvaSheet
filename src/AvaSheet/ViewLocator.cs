using Avalonia.Controls;
using Avalonia.Controls.Templates;
using AvaSheet.ViewModels;
using AvaSheet.Views;

namespace AvaSheet;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
///
/// 刻意不用「类型名替换 + Type.GetType + Activator」那套反射：
/// 裁剪（Trim）与 Native AOT 下这些成员会被去掉，Avalonia 的默认实现因此带
/// RequiresUnreferencedCode。这里改成显式映射，新增页面时在这里补一行即可。
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            HomeViewModel => new HomeView(),
            ExamViewModel => new ExamView(),
            PracticeViewModel => new PracticeView(),
            QuestionManageViewModel => new QuestionManageView(),
            ExamRecordsViewModel => new ExamRecordsView(),
            null => null,
            _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName },
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}

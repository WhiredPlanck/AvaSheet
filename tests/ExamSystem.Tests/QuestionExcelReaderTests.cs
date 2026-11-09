using System.Text;
using ExamSystem.Models;
using ExamSystem.Services;
using Sylvan.Data.Excel;
using Xunit;

namespace ExamSystem.Tests;

public sealed class QuestionExcelReaderTests
{
    private static string[] Header => ["类型", "题干", "A", "B", "C", "答案", "解析", "难度", "标签"];

    [Fact]
    public void Read_MapsColumnsAndBuildsAllQuestionTypes()
    {
        using var workbook = TestWorkbook.Create(
            ExcelWorkbookType.ExcelXml,
            Header,
            ["单选题", "首都是哪里", "北京", "上海", "", "A", "首都是北京", "简单", "地理，常识"],
            ["多选题", "哪些是偶数", "2", "3", "4", "A、C", "偶数是 2 的倍数", "中等", "数学"],
            ["判断题", "地球是圆的", "", "", "", "对", "", "较难", ""]);

        var result = QuestionExcelReader.Read(workbook);

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Questions.Count);

        var single = result.Questions[0];
        Assert.Equal(QuestionType.SingleChoice, single.Type);
        Assert.Equal("首都是哪里", single.Stem);
        Assert.Equal(new[] { "A", "B" }, single.Choices.Select(c => c.Key));
        Assert.Equal(new[] { "A" }, single.Answers);
        Assert.Equal("首都是北京", single.Explanation);
        Assert.Equal(1, single.Difficulty);
        Assert.Equal(new[] { "地理", "常识" }, single.Tags);

        var multiple = result.Questions[1];
        Assert.Equal(QuestionType.MultipleChoice, multiple.Type);
        Assert.Equal(new[] { "A", "C" }, multiple.Answers);
        Assert.Equal(3, multiple.Difficulty);

        var trueFalse = result.Questions[2];
        Assert.Equal(QuestionType.TrueFalse, trueFalse.Type);
        Assert.Empty(trueFalse.Choices);
        Assert.Equal(new[] { "T" }, trueFalse.Answers);
        Assert.Equal(4, trueFalse.Difficulty);
    }

    [Fact]
    public void Read_SkipsBlankRowsBeforeHeaderAndMatchesHeaderAliases()
    {
        using var workbook = TestWorkbook.Create(
            ExcelWorkbookType.ExcelXml,
            [],
            ["   "],
            ["题目类型", " 题 干 ", "选项A", "选项B", "参考答案", "难度"],
            ["判断", "水在标准大气压下 100 度沸腾", "", "", "√", "容易"]);

        var result = QuestionExcelReader.Read(workbook);

        Assert.Empty(result.Errors);
        var question = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.TrueFalse, question.Type);
        Assert.Equal("水在标准大气压下 100 度沸腾", question.Stem);
        Assert.Equal(new[] { "T" }, question.Answers);
        Assert.Equal(2, question.Difficulty);
    }

    [Fact]
    public void Read_ReportsEveryBadRow()
    {
        using var workbook = TestWorkbook.Create(
            ExcelWorkbookType.ExcelXml,
            Header,
            ["", "题型为空", "甲", "乙", "", "A", "", "", ""],
            ["选择", "认不出的题型", "甲", "乙", "", "A", "", "", ""],
            ["单选题", "", "甲", "乙", "", "A", "", "", ""],
            ["单选题", "答案为空", "甲", "乙", "", "", "", "", ""],
            ["单选题", "只有一个选项", "甲", "", "", "A", "", "", ""],
            ["单选题", "答案不在选项里", "甲", "乙", "", "C", "", "", ""],
            ["单选题", "单选给了两个答案", "甲", "乙", "", "A,B", "", "", ""],
            ["多选题", "多选只给一个答案", "甲", "乙", "", "A,A", "", "", ""]);

        var result = QuestionExcelReader.Read(workbook);

        Assert.Empty(result.Questions);
        Assert.Equal(8, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Message == "题型为空。");
        Assert.Contains(result.Errors, e => e.Message == "认不出题型「选择」。");
        Assert.Contains(result.Errors, e => e.Message == "题干为空。");
        Assert.Contains(result.Errors, e => e.Message == "答案为空。");
        Assert.Contains(result.Errors, e => e.Message == "选项少于两个。");
        Assert.Contains(result.Errors, e => e.Message == "答案 C 不在选项里。");
        Assert.Contains(result.Errors, e => e.Message == "单选题需要且只能有一个正确答案。");
        Assert.Contains(result.Errors, e => e.Message == "多选题至少要有两个正确答案。");
        Assert.All(result.Errors, e => Assert.True(e.Row >= 2));
    }

    [Fact]
    public void Read_WithoutHeaderRowThrows()
    {
        using var workbook = TestWorkbook.Create(
            ExcelWorkbookType.ExcelXml,
            ["单选题", "没有表头", "甲", "乙", "A"]);

        var error = Assert.Throws<InvalidDataException>(() => QuestionExcelReader.Read(workbook));

        Assert.Contains("没有找到表头行", error.Message);
    }

    [Fact]
    public void Read_GarbageStreamThrowsInvalidData()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("这不是表格"));

        var error = Assert.Throws<InvalidDataException>(
            () => QuestionExcelReader.Read(stream, ExcelWorkbookType.ExcelXml));

        Assert.Equal("这不是有效的 Excel 文件。", error.Message);
    }

    [Fact]
    public void DetectWorkbookType_UsesExtensionThenFileHeader()
    {
        using var xlsx = TestWorkbook.Create(ExcelWorkbookType.ExcelXml, Header, ["单选题", "题", "甲", "乙", "", "A"]);

        Assert.Null(QuestionExcelReader.DetectWorkbookType(xlsx, "questions.json"));
        Assert.Equal(ExcelWorkbookType.ExcelBinary, QuestionExcelReader.DetectWorkbookType(xlsx, "questions.xlsb"));
        Assert.Equal(ExcelWorkbookType.ExcelXml, QuestionExcelReader.DetectWorkbookType(xlsx, "questions.xlsm"));
        Assert.Equal(ExcelWorkbookType.Excel, QuestionExcelReader.DetectWorkbookType(xlsx, "questions.xls"));
        Assert.Equal(ExcelWorkbookType.ExcelXml, QuestionExcelReader.DetectWorkbookType(xlsx, null));
        Assert.Equal(ExcelWorkbookType.ExcelXml, QuestionExcelReader.DetectWorkbookType(xlsx, "questions.unknown"));

        using var ole = new MemoryStream([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0]);
        Assert.Equal(ExcelWorkbookType.Excel, QuestionExcelReader.DetectWorkbookType(ole, null));

        using var text = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));
        Assert.Null(QuestionExcelReader.DetectWorkbookType(text, null));
    }

    [Fact]
    public void Read_ReadsBinaryWorkbook()
    {
        using var workbook = TestWorkbook.Create(
            ExcelWorkbookType.ExcelBinary,
            Header,
            ["判断题", "二进制表格里的判断题", "", "", "", "错误", "", "困难", "常识"]);

        var result = QuestionExcelReader.Read(workbook, ExcelWorkbookType.ExcelBinary);

        var question = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.TrueFalse, question.Type);
        Assert.Equal(new[] { "F" }, question.Answers);
        Assert.Equal(5, question.Difficulty);
    }
}

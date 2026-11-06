using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ExamSystem.Models;
using Sylvan.Data.Excel;

namespace ExamSystem.Services;

/// <summary>表格里某一行的导入失败原因。</summary>
public sealed record ExcelRowError(int Row, string Message);

/// <summary>一次 Excel 导入的结果：成功的题目 + 被跳过的行。</summary>
public sealed record ExcelQuestionImportResult(
    IReadOnlyList<Question> Questions,
    IReadOnlyList<ExcelRowError> Errors);

/// <summary>
/// 读 Excel 里的题目：文件解析交给 <see cref="ExcelDataReader"/>（.xlsx / .xls / .xlsb），
/// 这里只负责找表头、按列名取单元格、校验并组装题目。
/// </summary>
public static class QuestionExcelReader
{
    private const int MaxChoices = 8;
    private const int HeaderRowScanLimit = 10;

    private static readonly char[] AnswerSeparators = [',', '，', '、', ';', '；', ' ', '\t', '/', '|'];
    private static readonly char[] TagSeparators = [',', '，', '、', ';', '；'];

    /// <summary>OLE2 / BIFF8 文件头，老式 .xls 用它。</summary>
    private static readonly byte[] OleMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>
    /// 判断该按哪种表格格式读。返回 null 表示不是表格（交给 JSON 解析）。
    /// 扩展名不认识时看文件头：OLE2 是 .xls，zip 里带 xl/workbook.bin 是 .xlsb，其余 zip 是 .xlsx。
    /// </summary>
    public static ExcelWorkbookType? DetectWorkbookType(Stream stream, string? fileName)
    {
        if (fileName is { Length: > 0 })
        {
            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (fileName.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase))
            {
                return ExcelWorkbookType.ExcelBinary;
            }

            if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xltx", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xltm", StringComparison.OrdinalIgnoreCase))
            {
                return ExcelWorkbookType.ExcelXml;
            }

            if (fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            {
                return ExcelWorkbookType.Excel;
            }
        }

        if (!stream.CanSeek)
        {
            // 读不了文件头就没法判断；按最常见的 .xlsx 试着读，读不出来会给出「不是有效的 Excel 文件」。
            return ExcelWorkbookType.ExcelXml;
        }

        var start = stream.Position;
        Span<byte> head = stackalloc byte[8];
        var read = stream.Read(head);
        stream.Position = start;

        if (read >= OleMagic.Length && head.SequenceEqual(OleMagic))
        {
            return ExcelWorkbookType.Excel;
        }

        if (read >= 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4)
        {
            return HasBinaryWorkbook(stream, start) ? ExcelWorkbookType.ExcelBinary : ExcelWorkbookType.ExcelXml;
        }

        return null;
    }

    /// <summary>读取第一个工作表并按表头映射成题目，格式按文件内容判断。</summary>
    public static ExcelQuestionImportResult Read(Stream stream)
        => Read(stream, DetectWorkbookType(stream, null) ?? ExcelWorkbookType.ExcelXml);

    /// <summary>按指定格式读取第一个工作表并按表头映射成题目。</summary>
    public static ExcelQuestionImportResult Read(Stream stream, ExcelWorkbookType type)
        => BuildQuestions(ReadRows(stream, type));

    private static bool HasBinaryWorkbook(Stream stream, long start)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return archive.Entries.Any(
                e => string.Equals(e.FullName, "xl/workbook.bin", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            stream.Position = start;
        }
    }

    // ---------- 单元格 ----------

    private sealed class SheetRow(int number, Dictionary<int, string> cells)
    {
        public int Number { get; } = number;

        /// <summary>列号（0 = A）到文本。</summary>
        public Dictionary<int, string> Cells { get; } = cells;

        public string Get(int? column) => column is { } c && Cells.TryGetValue(c, out var text) ? text : "";

        public bool IsBlank => Cells.Values.All(string.IsNullOrWhiteSpace);
    }

    private static List<SheetRow> ReadRows(Stream stream, ExcelWorkbookType type)
    {
        if (type == ExcelWorkbookType.Excel)
        {
            EnsureLegacyEncodings();
        }

        try
        {
            using var reader = ExcelDataReader.Create(stream, type, new ExcelDataReaderOptions
            {
                // 错误单元格（#N/A 之类）当空处理，不要因为一格错误让整次导入失败。
                FormulaErrorHandling = FormulaErrorHandling.Null,
                // 流由调用方关闭。
                OwnsStream = false,
                // 表头行也是数据：我们自己按列名找表头，不需要库替我们跳过第一行。
                Schema = ExcelSchema.NoHeaders,
            });

            var rows = new List<SheetRow>();

            // reader 初始停在第一个工作表，NextResult() 才会换表。
            while (reader.Read())
            {
                var cells = new Dictionary<int, string>();
                for (var column = 0; column < reader.RowFieldCount; column++)
                {
                    var text = CellText(reader, column);
                    if (!string.IsNullOrEmpty(text))
                    {
                        cells[column] = text;
                    }
                }

                rows.Add(new SheetRow(reader.RowNumber, cells));
            }

            return rows;
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("这不是有效的 Excel 文件。", ex);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("这不是有效的 Excel 文件。", ex);
        }
    }

    private static bool _legacyEncodingsRegistered;

    /// <summary>
    /// 老式 .xls（BIFF）按代码页存字符串（1252 / 936 等），.NET 默认不带这些编码。
    /// 只导 .xls 时才注册，避免影响 .xlsx 路径。
    /// </summary>
    private static void EnsureLegacyEncodings()
    {
        if (_legacyEncodingsRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _legacyEncodingsRegistered = true;
    }

    private static string CellText(ExcelDataReader reader, int column)
    {
        var kind = reader.GetExcelDataType(column);
        if (kind is ExcelDataType.Null or ExcelDataType.Error)
        {
            return "";
        }

        var text = reader.GetString(column);

        // 布尔单元格读出来是 True / False，转成和旧解析一致的 1 / 0。
        return kind == ExcelDataType.Boolean
            ? string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "1" : "0"
            : text;
    }

    // ---------- 表头 ----------

    private sealed class ColumnMap
    {
        public int? Type { get; set; }
        public int? Stem { get; set; }
        public int? Answer { get; set; }
        public int? Explanation { get; set; }
        public int? Difficulty { get; set; }
        public int? Tags { get; set; }

        /// <summary>选项列：选项字母（A–H）到列号，按列号升序。</summary>
        public SortedDictionary<char, int> Choices { get; } = [];
    }

    private static ExcelQuestionImportResult BuildQuestions(List<SheetRow> rows)
    {
        var headerIndex = -1;
        ColumnMap? map = null;
        var scanned = 0;

        for (var i = 0; i < rows.Count && scanned < HeaderRowScanLimit; i++)
        {
            if (rows[i].IsBlank)
            {
                continue;
            }

            scanned++;

            var candidate = MapHeader(rows[i]);
            if (candidate.Stem is null || candidate.Answer is null)
            {
                continue;
            }

            headerIndex = i;
            map = candidate;
            break;
        }

        if (map is null)
        {
            throw new InvalidDataException(
                "没有找到表头行。第一行需要包含「题型」「题干」「答案」这样的列名。");
        }

        var questions = new List<Question>();
        var errors = new List<ExcelRowError>();

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.IsBlank)
            {
                continue;
            }

            if (TryBuildQuestion(row, map, out var question, out var error))
            {
                questions.Add(question);
            }
            else
            {
                errors.Add(new ExcelRowError(row.Number, error));
            }
        }

        return new ExcelQuestionImportResult(questions, errors);
    }

    private static ColumnMap MapHeader(SheetRow row)
    {
        var map = new ColumnMap();

        foreach (var (column, raw) in row.Cells)
        {
            var header = NormalizeHeader(raw);
            if (header.Length == 0)
            {
                continue;
            }

            if (map.Type is null && header is "题型" or "类型" or "题目类型")
            {
                map.Type = column;
            }
            else if (map.Stem is null && header is "题干" or "题目" or "问题" or "题目内容")
            {
                map.Stem = column;
            }
            else if (map.Answer is null && header is "答案" or "正确答案" or "参考答案")
            {
                map.Answer = column;
            }
            else if (map.Explanation is null && header is "解析" or "答案解析" or "讲解")
            {
                map.Explanation = column;
            }
            else if (map.Difficulty is null && header is "难度" or "难易度")
            {
                map.Difficulty = column;
            }
            else if (map.Tags is null && header is "标签" or "分类" or "知识点")
            {
                map.Tags = column;
            }
            else if (map.Choices.Count < MaxChoices && TryChoiceLetter(header) is { } letter)
            {
                map.Choices.TryAdd(letter, column);
            }
        }

        return map;
    }

    private static string NormalizeHeader(string raw) => new(raw.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

    private static char? TryChoiceLetter(string header)
    {
        switch (header)
        {
            case [>= 'A' and <= 'H']:
                return header[0];
            case [>= 'a' and <= 'h']:
                return char.ToUpperInvariant(header[0]);
        }

        // 「选项A」「A选项」
        if (header.StartsWith("选项", StringComparison.Ordinal) && header.Length == 3
            && char.ToUpperInvariant(header[2]) is >= 'A' and <= 'H')
        {
            return char.ToUpperInvariant(header[2]);
        }

        if (header.EndsWith("选项", StringComparison.Ordinal) && header.Length == 3
            && char.ToUpperInvariant(header[0]) is >= 'A' and <= 'H')
        {
            return char.ToUpperInvariant(header[0]);
        }

        return null;
    }

    // ---------- 数据行 ----------

    private static bool TryBuildQuestion(
        SheetRow row,
        ColumnMap map,
        out Question question,
        out string error)
    {
        question = new Question();
        error = "";

        var typeText = row.Get(map.Type).Trim();
        if (typeText.Length == 0)
        {
            error = "题型为空。";
            return false;
        }

        if (!QuestionTypeExtensions.TryParseLabel(typeText, out var type))
        {
            error = $"认不出题型「{typeText}」。";
            return false;
        }

        var stem = row.Get(map.Stem).Trim();
        if (stem.Length == 0)
        {
            error = "题干为空。";
            return false;
        }

        var answerText = row.Get(map.Answer).Trim();
        if (answerText.Length == 0)
        {
            error = "答案为空。";
            return false;
        }

        var choices = new List<QuestionChoice>();
        foreach (var (letter, column) in map.Choices)
        {
            var text = row.Get(column).Trim();
            if (text.Length > 0)
            {
                choices.Add(new QuestionChoice { Key = letter.ToString(), Text = text });
            }
        }

        List<string> answers;

        if (type == QuestionType.TrueFalse)
        {
            if (!TryParseTrueFalse(answerText, out var trueFalseAnswer))
            {
                error = $"判断题的答案「{answerText}」认不出来，请写正确或错误。";
                return false;
            }

            // 判断题不存选项，只看答案。
            choices.Clear();
            answers = [trueFalseAnswer];
        }
        else
        {
            if (choices.Count < 2)
            {
                error = "选项少于两个。";
                return false;
            }

            var keys = ParseAnswerKeys(answerText);
            if (keys.Count == 0)
            {
                error = $"答案「{answerText}」里没有认出选项标识。";
                return false;
            }

            var available = choices.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var distinct = new List<string>();

            foreach (var key in keys)
            {
                if (!available.TryGetValue(key, out var actual))
                {
                    error = $"答案 {key} 不在选项里。";
                    return false;
                }

                if (!distinct.Contains(actual, StringComparer.OrdinalIgnoreCase))
                {
                    distinct.Add(actual);
                }
            }

            // 单选按用户写出的标识个数判断，写成 A,A 也算错。
            if (type == QuestionType.SingleChoice && keys.Count != 1)
            {
                error = "单选题需要且只能有一个正确答案。";
                return false;
            }

            // 多选按去重后的答案个数判断，A,A 只有一个有效答案。
            if (type == QuestionType.MultipleChoice && distinct.Count < 2)
            {
                error = "多选题至少要有两个正确答案。";
                return false;
            }

            answers = distinct;
        }

        question = new Question
        {
            Type = type,
            Stem = stem,
            Choices = choices,
            Answers = answers,
        };

        var explanation = row.Get(map.Explanation).Trim();
        question.Explanation = explanation.Length == 0 ? null : explanation;
        question.Difficulty = ParseDifficulty(row.Get(map.Difficulty));
        question.Tags = ParseTags(row.Get(map.Tags));

        return true;
    }

    private static List<string> ParseAnswerKeys(string text)
    {
        var keys = new List<string>();

        foreach (var token in NormalizeFullWidth(text).Split(AnswerSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var value = token.Trim();
            switch (value.Length)
            {
                case 0:
                    continue;
                case > 1 when value.All(ch => ch is >= 'A' and <= 'H'):
                    keys.AddRange(value.Select(ch => ch.ToString()));
                    break;
                case 1 when char.ToUpperInvariant(value[0]) is >= 'A' and <= 'H':
                    keys.Add(char.ToUpperInvariant(value[0]).ToString());
                    break;
                default:
                    return [];
            }
        }

        return keys;
    }

    private static bool TryParseTrueFalse(string text, out string answer)
    {
        answer = "T";
        var value = NormalizeFullWidth(text).Trim().ToLowerInvariant();

        switch (value)
        {
            case "正确" or "对" or "是" or "√" or "✓" or "t" or "true" or "1" or "y" or "yes":
                answer = "T";
                return true;
            case "错误" or "错" or "否" or "×" or "✗" or "x" or "f" or "false" or "0" or "n" or "no":
                answer = "F";
                return true;
            default:
                return false;
        }
    }

    private static int ParseDifficulty(string text)
    {
        var value = NormalizeFullWidth(text).Trim();
        if (value.Length == 0)
        {
            return 2;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Math.Clamp(number, 1, 5);
        }

        return value switch
        {
            "简单" or "很容易" or "易" => 1,
            "较易" or "容易" or "偏易" => 2,
            "中等" or "一般" or "适中" => 3,
            "较难" or "偏难" or "难" => 4,
            "困难" or "很难" => 5,
            _ => 2,
        };
    }

    private static List<string> ParseTags(string text)
    {
        var tags = new List<string>();
        foreach (var token in NormalizeFullWidth(text).Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var value = token.Trim();
            if (value.Length > 0 && !tags.Contains(value, StringComparer.Ordinal))
            {
                tags.Add(value);
            }
        }

        return tags;
    }

    /// <summary>全角字母、数字、常见符号转半角，方便识别手工录入的答案。</summary>
    private static string NormalizeFullWidth(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                >= 'Ａ' and <= 'Ｚ' => (char)(ch - 'Ａ' + 'A'),
                >= 'ａ' and <= 'ｚ' => (char)(ch - 'ａ' + 'a'),
                >= '０' and <= '９' => (char)(ch - '０' + '0'),
                '　' => ' ',
                _ => ch,
            });
        }

        return builder.ToString();
    }
}

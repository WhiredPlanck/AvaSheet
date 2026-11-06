using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>表格里某一行的导入失败原因。</summary>
public sealed record ExcelRowError(int Row, string Message);

/// <summary>一次 Excel 导入的结果：成功的题目 + 被跳过的行。</summary>
public sealed record ExcelQuestionImportResult(
    IReadOnlyList<Question> Questions,
    IReadOnlyList<ExcelRowError> Errors);

/// <summary>
/// 轻量 .xlsx 读取器：只用 <see cref="ZipArchive"/> + <see cref="XmlReader"/>，
/// 不依赖任何第三方库，也不依赖文件里的工作表顺序与列顺序。
/// </summary>
public static class QuestionExcelReader
{
    private const int MaxChoices = 8;
    private const int HeaderRowScanLimit = 10;

    private const string RelNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly char[] AnswerSeparators = [',', '，', '、', ';', '；', ' ', '\t', '/', '|'];
    private static readonly char[] TagSeparators = [',', '，', '、', ';', '；'];

    /// <summary>读取第一个工作表并按表头映射成题目。</summary>
    public static ExcelQuestionImportResult Read(Stream stream)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(
                "这不是有效的 Excel 文件。老式 .xls 请在 Excel 或 WPS 里另存为 .xlsx 再导入。");
        }

        using (archive)
        {
            var sharedStrings = ReadSharedStrings(archive);
            var sheetPath = ResolveFirstSheetPath(archive);
            var sheetEntry = FindEntry(archive, sheetPath)
                ?? throw new InvalidDataException("这个 Excel 文件里没有工作表。");

            var rows = ReadRows(sheetEntry, sharedStrings);
            return BuildQuestions(rows);
        }
    }

    // ---------- 包结构 ----------

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is not null)
        {
            return entry;
        }

        return archive.Entries.FirstOrDefault(
            e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveFirstSheetPath(ZipArchive archive)
    {
        const string fallback = "xl/worksheets/sheet1.xml";

        var workbookEntry = FindEntry(archive, "xl/workbook.xml");
        if (workbookEntry is null)
        {
            return fallback;
        }

        XDocument workbook;
        try
        {
            workbook = LoadXml(workbookEntry);
        }
        catch (XmlException)
        {
            throw new InvalidDataException("这不是有效的 Excel 文件。");
        }

        var sheet = workbook.Descendants().FirstOrDefault(e => e.Name.LocalName == "sheet");
        var relationId = sheet?.Attribute(XName.Get("id", RelNamespace))?.Value;
        if (string.IsNullOrEmpty(relationId))
        {
            return fallback;
        }

        var relsEntry = FindEntry(archive, "xl/_rels/workbook.xml.rels");
        if (relsEntry is null)
        {
            return fallback;
        }

        XDocument rels;
        try
        {
            rels = LoadXml(relsEntry);
        }
        catch (XmlException)
        {
            return fallback;
        }

        var target = rels.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Relationship"
                                 && string.Equals(e.Attribute("Id")?.Value, relationId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value;

        if (string.IsNullOrWhiteSpace(target))
        {
            return fallback;
        }

        target = target.Replace('\\', '/');
        if (target.StartsWith('/'))
        {
            return target.TrimStart('/');
        }

        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
        });
        return XDocument.Load(reader);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = FindEntry(archive, "xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        XDocument document;
        try
        {
            document = LoadXml(entry);
        }
        catch (XmlException)
        {
            return [];
        }

        return
        [
            .. document.Descendants()
                .Where(e => e.Name.LocalName == "si")
                .Select(ItemText)
        ];
    }

    /// <summary>共享字符串或内联字符串的文本；富文本由多个 &lt;r&gt;&lt;t&gt; 拼起来。</summary>
    private static string ItemText(XElement item)
        => string.Concat(item.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));

    // ---------- 单元格 ----------

    private sealed class SheetRow(int number, Dictionary<int, string> cells)
    {
        public int Number { get; } = number;

        /// <summary>列号（0 = A）到文本。</summary>
        public Dictionary<int, string> Cells { get; } = cells;

        public string Get(int? column) => column is { } c && Cells.TryGetValue(c, out var text) ? text : "";
    }

    private static List<SheetRow> ReadRows(ZipArchiveEntry entry, List<string> sharedStrings)
    {
        var rows = new List<SheetRow>();

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
        });

        while (!reader.EOF)
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "row", IsEmptyElement: false })
            {
                // ReadFrom 会把读取器移到该元素之后，所以这里不能再次 Read()，
                // 否则会跳过紧随其后的下一个 <row>。
                var rowElement = (XElement)XNode.ReadFrom(reader);
                rows.Add(ParseRow(rowElement, sharedStrings, rows.Count + 1));
                continue;
            }

            reader.Read();
        }

        return rows;
    }

    private static SheetRow ParseRow(XElement rowElement, List<string> sharedStrings, int fallbackNumber)
    {
        var number = int.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallbackNumber;

        var cells = new Dictionary<int, string>();
        var nextColumn = 0;

        foreach (var cell in rowElement.Elements().Where(e => e.Name.LocalName == "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var column = reference is null ? nextColumn : ColumnIndex(reference);
            nextColumn = column + 1;

            cells[column] = CellText(cell, sharedStrings);
        }

        return new SheetRow(number, cells);
    }

    private static string CellText(XElement cell, List<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;

        switch (type)
        {
            case "s":
            {
                var indexText = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
                return int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                       && index >= 0 && index < sharedStrings.Count
                    ? sharedStrings[index]
                    : "";
            }
            case "inlineStr":
            {
                var inline = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
                return inline is null ? "" : ItemText(inline);
            }
            case "b":
                return cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value == "1" ? "1" : "0";
            default:
                // t="str"（公式结果）与无类型（数字）都直接取 <v>，公式的缓存值也在这里。
                return cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
        }
    }

    /// <summary>把 "AB12" 里的列字母转成 0 起的列号。</summary>
    private static int ColumnIndex(string reference)
    {
        var column = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                column = column * 26 + (ch - 'A' + 1);
            }
            else if (ch is >= 'a' and <= 'z')
            {
                column = column * 26 + (ch - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return Math.Max(0, column - 1);
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

        for (var i = 0; i < rows.Count && i < HeaderRowScanLimit; i++)
        {
            var candidate = MapHeader(rows[i]);
            if (candidate.Stem is not null && candidate.Answer is not null)
            {
                headerIndex = i;
                map = candidate;
                break;
            }
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
            if (row.Cells.Values.All(string.IsNullOrWhiteSpace))
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

    private static string NormalizeHeader(string raw)
        => new string(raw.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

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
            var missing = keys.FirstOrDefault(k => !available.Contains(k));
            if (missing is not null)
            {
                error = $"答案 {missing} 不在选项里。";
                return false;
            }

            if (type == QuestionType.SingleChoice && keys.Count != 1)
            {
                error = "单选题需要且只能有一个正确答案。";
                return false;
            }

            if (type == QuestionType.MultipleChoice && keys.Count < 2)
            {
                error = "多选题至少要有两个正确答案。";
                return false;
            }

            answers = [];
            foreach (var key in keys)
            {
                var actual = choices.First(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)).Key;
                if (!answers.Contains(actual, StringComparer.OrdinalIgnoreCase))
                {
                    answers.Add(actual);
                }
            }
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
            if (value.Length == 0)
            {
                continue;
            }

            if (value.Length > 1 && value.All(ch => ch is >= 'A' and <= 'H'))
            {
                keys.AddRange(value.Select(ch => ch.ToString()));
            }
            else if (value.Length == 1 && char.ToUpperInvariant(value[0]) is >= 'A' and <= 'H')
            {
                keys.Add(char.ToUpperInvariant(value[0]).ToString());
            }
            else
            {
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

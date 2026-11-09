using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AvaSheet.Models;

namespace AvaSheet.Services;

/// <summary>
/// 源生成的序列化上下文：不用反射，启动更快、裁剪/AOT 更安全。
/// 输出 camelCase、缩进、枚举写成名字（"SingleChoice" 而不是 0）。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(QuestionBank))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Question))]
[JsonSerializable(typeof(List<Question>))]
[JsonSerializable(typeof(ExamRecord))]
[JsonSerializable(typeof(List<ExamRecord>))]
[JsonSerializable(typeof(BankProgress))]
internal sealed partial class JsonContext : JsonSerializerContext
{
    /// <summary>
    /// 给人类看的题库文件：中文不转义成 \uXXXX，带缩进。
    /// 以源生成上下文为解析器，仍然是裁剪/AOT 安全的。
    /// 必须惰性初始化：静态字段按文本顺序执行，这里若在类型初始化阶段就读 Default，
    /// 拿到的会是 null，退回成"无缩进、PascalCase、枚举数字"的默认配置。
    /// </summary>
    public static JsonSerializerOptions Io => field ??= CreateIoOptions();

    private static JsonSerializerOptions CreateIoOptions()
    {
        return new JsonSerializerOptions(Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    // 下面这组 JsonTypeInfo 供 JsonSerializer/JsonNode 使用。
    // 调用带 JsonSerializerOptions 的泛型重载会命中 RequiresUnreferencedCode /
    // RequiresDynamicCode（IL2026 / IL3050），带 JsonTypeInfo 的重载则不会：
    // 类型信息全部来自源生成上下文，裁剪后不需要任何反射或运行时代码生成。

    public static JsonTypeInfo<QuestionBank> QuestionBankInfo => field ??= Get<QuestionBank>();

    public static JsonTypeInfo<AppSettings> AppSettingsInfo => field ??= Get<AppSettings>();

    public static JsonTypeInfo<List<Question>> QuestionListInfo => field ??= Get<List<Question>>();

    public static JsonTypeInfo<List<ExamRecord>> ExamRecordListInfo => field ??= Get<List<ExamRecord>>();

    public static JsonTypeInfo<BankProgress> BankProgressInfo => field ??= Get<BankProgress>();

    private static JsonTypeInfo<T> Get<T>() => (JsonTypeInfo<T>)Io.GetTypeInfo(typeof(T))!;
}

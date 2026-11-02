using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExamSystem.Models;

namespace ExamSystem.Services;

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
internal sealed partial class JsonContext : JsonSerializerContext
{
    /// <summary>
    /// 给人类看的题库文件：中文不转义成 \uXXXX，带缩进。
    /// 以源生成上下文为解析器，仍然是裁剪/AOT 安全的。
    /// 必须惰性初始化：静态字段按文本顺序执行，这里若在类型初始化阶段就读 Default，
    /// 拿到的会是 null，退回成"无缩进、PascalCase、枚举数字"的默认配置。
    /// </summary>
    public static JsonSerializerOptions Io => _io ??= CreateIoOptions();

    private static JsonSerializerOptions? _io;

    private static JsonSerializerOptions CreateIoOptions()
    {
        return new JsonSerializerOptions(Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaSheet.Services;

/// <summary>
/// 原子写文件：同目录临时文件 → 写入 → 刷到磁盘 → 改名覆盖。
///
/// 只做 FlushAsync 只是把数据交给操作系统的页缓存，改名先成功、内容还在缓存里的情况下
/// 突然掉电，会留下一个「存在但是空的」文件。所以改名之前再 Flush(flushToDisk: true)。
/// </summary>
public static class AtomicFile
{
    private const string TempSuffix = ".tmp";
    private const int BufferSize = 64 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var temp = path + TempSuffix;
        var bytes = Utf8NoBom.GetBytes(content);

        await using (var stream = new FileStream(
                         temp,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}

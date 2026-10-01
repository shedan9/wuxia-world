using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WuxiaWorld.Infrastructure.Content;

/// <summary>内容源目录的公共读取：文件枚举次序、内容版本与文本表合并。</summary>
public static class ContentFiles
{
    /// <summary>内容目录下全部 JSON 文件的相对路径（正斜杠），按序数排序。</summary>
    public static IEnumerable<string> Enumerate(string root) =>
        Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// 内容版本（<c>ContentManifest.content_version</c>）：全部 JSON 文件“相对路径 + 内容（统一换行）”的 SHA-256 前 16 位。
    /// 战斗与世界内容包共用这一个版本，存档与战斗记录据此比对。
    /// </summary>
    public static string Version(string root)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Enumerate(root))
        {
            var json = File.ReadAllText(Path.Combine(root, file), Encoding.UTF8);
            sha.AppendData(Encoding.UTF8.GetBytes(file + "\n"));
            sha.AppendData(Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n", StringComparison.Ordinal)));
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }

    /// <summary>把一个文本表文件并入总表；键重复报错。</summary>
    public static void MergeText(IDictionary<string, string> text, string json)
    {
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ContentJson.Options)
            ?? throw new InvalidDataException("文件内容为空。");
        foreach (var (k, v) in table)
        {
            if (!text.TryAdd(k, v))
            {
                throw new InvalidDataException($"文本键重复：{k}");
            }
        }
    }
}

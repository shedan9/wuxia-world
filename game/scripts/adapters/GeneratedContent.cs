using Godot;
using WuxiaWorld.Infrastructure.Content;

namespace WuxiaWorld.Game.Adapters;

/// <summary>
/// 读取内容编译器生成的内容包（<c>res://generated/content/</c>，由 tools/ContentCompiler 写出，禁止手改）。
/// 经 Godot 文件接口读取，导出包内同样可用；解析与校验交给 Infrastructure（架构文档 5.3）。
/// </summary>
public static class GeneratedContent
{
    private const string Dir = "res://generated/content/";
    private static CombatBundle? _combat;
    private static string? _error;

    /// <summary>战斗内容包；读取失败返回 null，原因见 <see cref="Error"/>。</summary>
    public static CombatBundle? Combat
    {
        get
        {
            if (_combat is not null || _error is not null)
            {
                return _combat;
            }

            var path = Dir + CombatBundle.FileName;
            if (!Godot.FileAccess.FileExists(path))
            {
                _error = $"找不到 {path}：请先运行 dotnet run --project tools/ContentCompiler 生成内容包。";
                return null;
            }

            try
            {
                _combat = CombatBundle.Parse(Godot.FileAccess.GetFileAsString(path));
            }
            catch (InvalidDataException ex)
            {
                _error = ex.Message;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _error = $"内容包格式错误：{ex.Message}";
            }

            return _combat;
        }
    }

    public static string? Error => _error;
}

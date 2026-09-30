using System.Text;
using WuxiaWorld.Infrastructure.Content;

// 用法：dotnet run --project tools/ContentCompiler -- [--content content] [--out game/generated/content] [--check]
// 读取 content/ 下的战斗内容，做语义校验；通过后写出合成包 combat.json。--check 只校验不写文件（CI 用）。
string? contentDir = null;
string? outDir = null;
var checkOnly = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--content": contentDir = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--check": checkOnly = true; break;
        default:
            Console.Error.WriteLine($"未知参数 {args[i]}");
            return 2;
    }
}

var root = FindRepoRoot();
contentDir ??= Path.Combine(root, "content");
outDir ??= Path.Combine(root, "game", "generated", "content");

CombatBundle bundle;
try
{
    bundle = CombatContentLoader.LoadDirectory(contentDir);
}
catch (Exception ex) when (ex is InvalidDataException or DirectoryNotFoundException)
{
    Console.Error.WriteLine($"读取失败：{ex.Message}");
    return 1;
}

var errors = CombatContentValidator.Validate(bundle);
if (errors.Count > 0)
{
    Console.Error.WriteLine($"内容校验失败（{errors.Count} 项）：");
    foreach (var e in errors)
    {
        Console.Error.WriteLine("  · " + e);
    }

    return 1;
}

Console.WriteLine($"校验通过：招式 {bundle.Skills.Count}、状态 {bundle.Statuses.Count}、心法 / 轻功 / 天赋 {bundle.Arts.Count}、" +
    $"物品 {bundle.Items.Count}、战斗单位 {bundle.Combatants.Count}、遭遇 {bundle.Encounters.Count}、文本 {bundle.Text.Count} 条；内容版本 {bundle.ContentVersion}");
if (checkOnly)
{
    return 0;
}

Directory.CreateDirectory(outDir);
var path = Path.Combine(outDir, CombatBundle.FileName);
File.WriteAllText(path, bundle.Serialize(), new UTF8Encoding(false));
Console.WriteLine($"已写出 {Path.GetRelativePath(root, path)}");
return 0;

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WuxiaWorld.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }
    }

    throw new DirectoryNotFoundException("找不到仓库根目录（WuxiaWorld.sln）。");
}

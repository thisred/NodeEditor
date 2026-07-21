using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NodeEditor.Helpers;

/// <summary>
/// 源码定位器 — 在源码树中查找节点类的定义文件与行号。
/// 用于"双击节点 → 在 IDE 中打开对应源码"。
/// 通过扫描 .cs 文件中的 class 定义实现，无需在节点类上额外标注。
/// </summary>
public static class SourceLocator
{
    /// <summary>缓存的源码根目录（空字符串表示未找到，避免重复向上遍历）</summary>
    private static string? _sourceRoot;

    /// <summary>
    /// 查找指定类名的定义位置。
    /// 返回 (文件绝对路径, 1-based 行号)；未找到时 Path 为 null。
    /// </summary>
    public static (string? Path, int Line) FindClassDefinition(string className)
    {
        if (string.IsNullOrEmpty(className)) return (null, 0);

        var root = GetSourceRoot();
        if (root == null) return (null, 0);

        // 匹配 class/struct/record 定义行（允许 public/abstract/sealed 等修饰符在前）
        var regex = new Regex($@"\b(?:class|struct|record)\s+{Regex.Escape(className)}\b");

        foreach (var file in EnumerateSourceFiles(root))
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                        return (file, i + 1);
                }
            }
            catch
            {
                // 忽略无法读取的文件
            }
        }

        return (null, 0);
    }

    /// <summary>
    /// 从运行时目录向上查找源码根目录（包含 .git 或 *.slnx / *.sln 的目录）。
    /// 应用从 IDE 的构建输出目录运行时，源码树就在其上级目录中。
    /// </summary>
    private static string? GetSourceRoot()
    {
        if (_sourceRoot != null)
            return _sourceRoot.Length > 0 ? _sourceRoot : null;

        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetDirectories(".git").Any() ||
                    dir.GetFiles("*.slnx").Any() ||
                    dir.GetFiles("*.sln").Any())
                {
                    _sourceRoot = dir.FullName;
                    return _sourceRoot;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // 忽略遍历异常
        }

        _sourceRoot = string.Empty; // 未找到，缓存空串避免重复遍历
        return null;
    }

    /// <summary>枚举源码树下的所有 .cs 文件（排除 bin / obj 输出目录）</summary>
    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"));
    }
}

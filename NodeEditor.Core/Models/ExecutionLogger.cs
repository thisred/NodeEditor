using System;
using System.Collections.Generic;

namespace NodeEditor.Core.Models;

/// <summary>
/// 执行日志服务 — 捕获节点执行过程中的输出信息，供 UI 层展示。
/// </summary>
public static class ExecutionLogger
{
    private static readonly List<LogEntry> _entries = new();

    public static IReadOnlyList<LogEntry> Entries => _entries;

    public static event Action<LogEntry>? LogAdded;
    public static event Action? Cleared;

    public static void Log(string message, string? nodeName = null)
    {
        var entry = new LogEntry(DateTime.Now, message, nodeName);
        _entries.Add(entry);
        LogAdded?.Invoke(entry);
    }

    public static void Clear()
    {
        _entries.Clear();
        Cleared?.Invoke();
    }
}

/// <summary>单条日志记录</summary>
public record LogEntry(DateTime Timestamp, string Message, string? NodeName);
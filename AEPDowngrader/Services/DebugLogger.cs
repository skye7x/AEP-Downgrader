using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AEPDowngrader.Services
{
    public static class DebugLevel
    {
        public const string Trace = "TRACE";
        public const string Debug = "DEBUG";
        public const string Info = "INFO";
        public const string Warning = "WARNING";
        public const string Error = "ERROR";
        public const string Critical = "CRITICAL";
    }

    /// <summary>Cross-platform system information collector, mirroring PlatformInfo.</summary>
    public static class PlatformInfo
    {
        public static Dictionary<string, string> GetPlatformInfo()
        {
            var info = new Dictionary<string, string>
            {
                ["system"] = Environment.OSVersion.Platform.ToString(),
                ["release"] = Environment.OSVersion.VersionString,
                ["version"] = Environment.OSVersion.Version.ToString(),
                ["machine"] = RuntimeInformation.OSArchitecture.ToString(),
                ["processor"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["dotnet_version"] = RuntimeInformation.FrameworkDescription,
                ["hostname"] = Environment.MachineName,
            };

            if (OperatingSystem.IsWindows())
            {
                info["windows_version"] = Environment.OSVersion.Version.ToString();
                info["windows_edition"] = RuntimeInformation.OSDescription;
            }

            return info;
        }
    }

    /// <summary>Memory and resource usage information collector, mirroring MemoryInfo.</summary>
    public static class MemoryInfo
    {
        public static Dictionary<string, object> GetMemoryInfo()
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                var info = new Dictionary<string, object>
                {
                    ["rss_mb"] = Math.Round(process.WorkingSet64 / (1024.0 * 1024.0), 2),
                    ["vms_mb"] = Math.Round(process.VirtualMemorySize64 / (1024.0 * 1024.0), 2),
                    ["num_threads"] = process.Threads.Count,
                };
                return info;
            }
            catch (Exception e)
            {
                return new Dictionary<string, object> { ["error"] = e.Message };
            }
        }

        public static Dictionary<string, object> GetCpuInfo()
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                return new Dictionary<string, object>
                {
                    ["cpu_time_ms"] = process.TotalProcessorTime.TotalMilliseconds,
                    ["num_threads"] = process.Threads.Count,
                    ["cpu_count"] = Environment.ProcessorCount,
                };
            }
            catch (Exception e)
            {
                return new Dictionary<string, object> { ["error"] = e.Message };
            }
        }
    }

    /// <summary>Record of a single logged file system operation, mirroring FileSystemMonitor entries.</summary>
    public class FileOperationEntry
    {
        public string Timestamp { get; set; } = "";
        public string Type { get; set; } = "";
        public string Path { get; set; } = "";
        public Dictionary<string, object?> Details { get; set; } = new();
        public int ThreadId { get; set; }
    }

    /// <summary>Monitor file system operations, mirroring FileSystemMonitor.</summary>
    public class FileSystemMonitor
    {
        private readonly List<FileOperationEntry> _operations = new();
        private readonly object _lock = new();

        public void LogOperation(string operationType, string path, Dictionary<string, object?>? details = null)
        {
            lock (_lock)
            {
                _operations.Add(new FileOperationEntry
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    Type = operationType,
                    Path = path,
                    Details = details ?? new Dictionary<string, object?>(),
                    ThreadId = Environment.CurrentManagedThreadId,
                });
            }
        }

        public void LogRead(string path, long? size = null) =>
            LogOperation("READ", path, size.HasValue ? new() { ["size"] = size.Value } : null);

        public void LogWrite(string path, long? size = null) =>
            LogOperation("WRITE", path, size.HasValue ? new() { ["size"] = size.Value } : null);

        public List<FileOperationEntry> GetOperations()
        {
            lock (_lock) { return new List<FileOperationEntry>(_operations); }
        }

        public void Clear()
        {
            lock (_lock) { _operations.Clear(); }
        }
    }

    /// <summary>
    /// Central debug logging facility, mirroring the DebugLogger class in debug_logger.py:
    /// buffered log entries, optional log file, file-operation tracking, and full report
    /// generation for export/clipboard.
    /// </summary>
    public class DebugLogger
    {
        private readonly StringBuilder _logBuffer = new();
        private readonly object _bufferLock = new();
        private StreamWriter? _logFile;
        private string? _sessionId;
        private DateTime? _startTime;
        private bool _enabled;

        public FileSystemMonitor FsMonitor { get; } = new();
        public Dictionary<string, string> PlatformInfoSnapshot { get; private set; } = new();

        public bool IsEnabled() => _enabled;

        /// <summary>Enable debug mode; opens a log file and returns its path.</summary>
        public string? Enable()
        {
            _enabled = true;
            _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _startTime = DateTime.Now;
            PlatformInfoSnapshot = PlatformInfo.GetPlatformInfo();

            try
            {
                string logDir = GetLogDirectory();
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, $"debug_{_sessionId}.log");
                _logFile = new StreamWriter(logPath, append: true) { AutoFlush = true };
                Info("Debug logging session started");
                return logPath;
            }
            catch
            {
                return null;
            }
        }

        public void Disable()
        {
            LogSummary();
            _enabled = false;
            _logFile?.Dispose();
            _logFile = null;
        }

        private static string GetLogDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AEPDowngrader", "logs");
        }

        private void Log(string level, string message, Dictionary<string, object?>? extraInfo = null)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            int threadId = Environment.CurrentManagedThreadId;

            string formatted = $"[{timestamp}] [{level}] [Thread-{threadId}] {message}";
            if (extraInfo != null && extraInfo.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in extraInfo)
                {
                    parts.Add($"\"{kv.Key}\": \"{kv.Value}\"");
                }
                formatted += " | {" + string.Join(", ", parts) + "}";
            }

            lock (_bufferLock)
            {
                _logBuffer.AppendLine(formatted);
            }

            _logFile?.WriteLine(formatted);
        }

        private void LogSummary()
        {
            Log(DebugLevel.Info, "=== Session Summary ===");
            if (_startTime.HasValue)
            {
                Log(DebugLevel.Info, $"Duration: {DateTime.Now - _startTime.Value}");
            }
            var ops = FsMonitor.GetOperations();
            Log(DebugLevel.Info, $"File operations: {ops.Count}");
            var mem = MemoryInfo.GetMemoryInfo();
            Log(DebugLevel.Info, $"Final memory (RSS): {(mem.TryGetValue("rss_mb", out var rss) ? rss : "N/A")} MB");
        }

        public void Trace(string message) { if (_enabled) Log(DebugLevel.Trace, message); }
        public void Debug(string message) { if (_enabled) Log(DebugLevel.Debug, message); }
        public void Info(string message) { if (_enabled) Log(DebugLevel.Info, message); }
        public void Warning(string message) { if (_enabled) Log(DebugLevel.Warning, message); }

        public void Error(string message)
        {
            if (!_enabled) return;
            Log(DebugLevel.Error, message);
            Log(DebugLevel.Error, $"Stack trace: {Environment.StackTrace}");
        }

        public void Critical(string message) { if (_enabled) Log(DebugLevel.Critical, message); }

        public void LogFunctionCall(string funcName, string? args = null)
        {
            if (!_enabled) return;
            Log(DebugLevel.Trace, $"Calling: {funcName}", new() { ["function"] = funcName, ["args"] = args ?? "" });
        }

        public void LogMemory(string label = "")
        {
            if (!_enabled) return;
            var mem = MemoryInfo.GetMemoryInfo();
            var extra = new Dictionary<string, object?>();
            foreach (var kv in mem) extra[kv.Key] = kv.Value;
            Log(DebugLevel.Debug, $"Memory ({label})", extra);
        }

        public void LogFileRead(string path, long? size = null)
        {
            if (!_enabled) return;
            FsMonitor.LogRead(path, size);
            Log(DebugLevel.Trace, $"Read file: {path}", new() { ["size"] = size });
        }

        public void LogFileWrite(string path, long? size = null)
        {
            if (!_enabled) return;
            FsMonitor.LogWrite(path, size);
            Log(DebugLevel.Trace, $"Write file: {path}", new() { ["size"] = size });
        }

        public void LogFileOperation(string operation, string path)
        {
            if (!_enabled) return;
            FsMonitor.LogOperation(operation, path);
            Log(DebugLevel.Trace, $"File operation: {operation} - {path}");
        }

        public string GetLogContent()
        {
            lock (_bufferLock) { return _logBuffer.ToString(); }
        }

        /// <summary>Generate full debug report with system info, mirroring get_full_report().</summary>
        public string GetFullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("AEP Downgrader - Debug Report");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            sb.AppendLine("SESSION INFORMATION");
            sb.AppendLine(new string('-', 40));
            sb.AppendLine($"Session ID: {_sessionId ?? "N/A"}");
            sb.AppendLine($"Start Time: {(_startTime.HasValue ? _startTime.Value.ToString() : "N/A")}");
            if (_startTime.HasValue)
            {
                sb.AppendLine($"Duration: {DateTime.Now - _startTime.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("SYSTEM INFORMATION");
            sb.AppendLine(new string('-', 40));
            foreach (var kv in PlatformInfoSnapshot)
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("MEMORY INFORMATION");
            sb.AppendLine(new string('-', 40));
            foreach (var kv in MemoryInfo.GetMemoryInfo())
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("CPU INFORMATION");
            sb.AppendLine(new string('-', 40));
            foreach (var kv in MemoryInfo.GetCpuInfo())
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("FILE OPERATIONS");
            sb.AppendLine(new string('-', 40));
            var ops = FsMonitor.GetOperations();
            sb.AppendLine($"Total operations: {ops.Count}");
            sb.AppendLine();
            int shown = 0;
            foreach (var op in ops)
            {
                if (shown >= 50) break;
                sb.AppendLine($"  [{op.Timestamp}] {op.Type}: {op.Path}");
                foreach (var kv in op.Details)
                {
                    sb.AppendLine($"    {kv.Key}: {kv.Value}");
                }
                shown++;
            }
            if (ops.Count > 50)
            {
                sb.AppendLine($"  ... and {ops.Count - 50} more operations");
            }
            sb.AppendLine();

            sb.AppendLine("LOG ENTRIES");
            sb.AppendLine(new string('-', 40));
            sb.Append(GetLogContent());

            return sb.ToString();
        }

        public string ExportLogs(string? filePath = null)
        {
            if (filePath == null)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string logDir = GetLogDirectory();
                Directory.CreateDirectory(logDir);
                filePath = Path.Combine(logDir, $"debug_export_{timestamp}.log");
            }

            File.WriteAllText(filePath, GetFullReport(), Encoding.UTF8);
            return filePath;
        }

        public void ClearLogs()
        {
            lock (_bufferLock) { _logBuffer.Clear(); }
            FsMonitor.Clear();
        }
    }

    /// <summary>Global debug logger instance, mirroring the module-level `debug_logger` singleton.</summary>
    public static class DebugLoggerInstance
    {
        public static readonly DebugLogger Instance = new();
    }
}

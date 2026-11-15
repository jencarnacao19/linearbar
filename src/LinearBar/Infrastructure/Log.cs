using System.IO;
using ManagedShell.Common.Logging;

namespace LinearBar.Infrastructure;

/// <summary>
/// Minimal file logger. Logging must never throw: a taskbar that dies because the disk is
/// full is worse than a taskbar without a log.
/// </summary>
internal static class Log
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static int _errorCount;
    private static int _shellErrorCount;

    /// <summary>Full path of the log file, once initialised.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>When false, <see cref="Debug"/> messages are dropped.</summary>
    public static bool Verbose { get; set; }

    /// <summary>Number of errors logged by LinearBar itself.</summary>
    public static int ErrorCount => Volatile.Read(ref _errorCount);

    /// <summary>Number of errors reported by the ManagedShell library.</summary>
    public static int ShellErrorCount => Volatile.Read(ref _shellErrorCount);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LinearBar",
        "logs",
        "linearbar.log");

    public static void Initialize(string? path, bool verbose)
    {
        Verbose = verbose;
        path = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultPath : path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Keep one previous log so the file cannot grow without bound.
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxFileBytes)
            {
                File.Move(path, path + ".old", overwrite: true);
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            lock (Gate)
            {
                _writer = new StreamWriter(stream) { AutoFlush = true };
                FilePath = path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine("LinearBar: cannot open log file: " + ex.Message);
        }
    }

    public static void Debug(string message)
    {
        if (Verbose)
        {
            Write("DBG", message, null);
        }
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);

    public static void Error(string message, Exception? exception = null)
    {
        Interlocked.Increment(ref _errorCount);
        Write("ERR", message, exception);
    }

    public static void Close()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        System.Diagnostics.Debug.WriteLine(line);

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Nothing sensible to do; see the class remarks.
            }
        }
    }

    /// <summary>Forwards ManagedShell's own log into ours, tagged so the source is clear.</summary>
    internal sealed class ShellLogBridge : ILog
    {
        public void Log(object sender, LogEventArgs e)
        {
            string message = "[ManagedShell] " + e.Message;
            switch (e.Severity)
            {
                case LogSeverity.Debug:
                    Debug(message);
                    break;
                case LogSeverity.Info:
                    Info(message);
                    break;
                case LogSeverity.Warning:
                    Warn(message, e.Exception);
                    break;
                default:
                    // Counted separately: library errors are reported, but are not ours to fail on.
                    Interlocked.Increment(ref _shellErrorCount);
                    Write("ERR", message, e.Exception);
                    break;
            }
        }
    }
}

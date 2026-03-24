using System;
using System.IO;
using System.Text;
using System.Threading;

namespace TradeRouter
{
    /// <summary>
    /// Thread-safe file logger with 10 MB rotation.
    /// Format: [2026-03-23 18:00:00] [INFO] message
    /// </summary>
    public sealed class FileLogger : IDisposable
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        private readonly string _logPath;
        private readonly string _backupPath;
        private readonly object _lock = new();
        private bool _enabled;
        private bool _disposed;

        public FileLogger(string baseDir)
        {
            _logPath    = Path.Combine(baseDir, "TradeRouter.log");
            _backupPath = Path.Combine(baseDir, "TradeRouter.log.bak");
        }

        public void SetEnabled(bool value) { lock (_lock) { _enabled = value; } }
        public bool GetEnabled()           { lock (_lock) { return _enabled; } }

        public void Info(string message)  => Write("INFO",  message);
        public void Warn(string message)  => Write("WARN",  message);
        public void Error(string message) => Write("ERROR", message);

        public void Write(string level, string message)
        {
            lock (_lock)
            {
                if (!_enabled) return;
                try
                {
                    RotateIfNeeded();
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                }
                catch
                {
                    // Never crash the app due to logging failure
                }
            }
        }

        private void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                var info = new FileInfo(_logPath);
                if (info.Length >= MaxFileSizeBytes)
                {
                    if (File.Exists(_backupPath))
                        File.Delete(_backupPath);
                    File.Move(_logPath, _backupPath);
                }
            }
            catch { /* best effort */ }
        }

        public void Dispose()
        {
            if (!_disposed)
                _disposed = true;
        }
    }
}

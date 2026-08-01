using System;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using AEPDowngrader.Services;

namespace AEPDowngrader.Views
{
    /// <summary>Dialog for viewing debug logs. Mirrors the DebugLogViewer QDialog.</summary>
    public partial class DebugLogViewerWindow : Window
    {
        private readonly DebugLogger _logger;

        public DebugLogViewerWindow(DebugLogger logger, Window owner)
        {
            InitializeComponent();
            _logger = logger;
            Owner = owner;
            RefreshLogs();
        }

        public void RefreshLogs()
        {
            LogTextBox.Text = _logger.GetLogContent();

            var sb = new StringBuilder();
            sb.AppendLine("SYSTEM INFORMATION");
            sb.AppendLine(new string('=', 40));
            sb.AppendLine();
            foreach (var kv in PlatformInfo.GetPlatformInfo())
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("MEMORY INFORMATION");
            sb.AppendLine(new string('=', 40));
            sb.AppendLine();
            foreach (var kv in MemoryInfo.GetMemoryInfo())
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            SystemInfoTextBox.Text = sb.ToString();

            var fileOps = new StringBuilder();
            fileOps.AppendLine("FILE OPERATIONS");
            fileOps.AppendLine(new string('=', 40));
            fileOps.AppendLine();
            foreach (var op in _logger.FsMonitor.GetOperations())
            {
                fileOps.AppendLine($"[{op.Timestamp}] {op.Type}: {op.Path}");
                foreach (var kv in op.Details)
                {
                    fileOps.AppendLine($"  {kv.Key}: {kv.Value}");
                }
            }
            FileOpsTextBox.Text = fileOps.ToString();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLogs();

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            string report = _logger.GetFullReport();
            Clipboard.SetText(report);
            MessageBox.Show(this, "Debug report copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportLogs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Debug Logs",
                FileName = $"debug_report_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                Filter = "Log Files (*.log)|*.log|Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
            {
                string exportedPath = _logger.ExportLogs(dialog.FileName);
                MessageBox.Show(this, $"Debug report exported to:\n{exportedPath}", "Exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this, "Are you sure you want to clear all debug logs?", "Clear Logs",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _logger.ClearLogs();
                RefreshLogs();
                MessageBox.Show(this, "Debug logs cleared!", "Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}

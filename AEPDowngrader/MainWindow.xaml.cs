using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using AEPDowngrader.Models;
using AEPDowngrader.Services;
using AEPDowngrader.Views;

namespace AEPDowngrader
{
    /// <summary>
    /// Main application window. Ported from AEPDowngraderGUI (QMainWindow) in AEPdowngrader.py,
    /// preserving the same version-detection, target-version population, conversion, and
    /// update-check behavior.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int MinAeVersion = AepConverter.MinAeVersion;
        private const int MaxAeVersion = AepConverter.MaxAeVersion;
        private static readonly HashSet<int> ExperimentalTargetVersions = AepConverter.ExperimentalTargetVersions;
        private const long UpdateCheckIntervalSeconds = 24 * 60 * 60;

        private readonly AppSettings _settings = new();
        private readonly DebugLogger _logger = DebugLoggerInstance.Instance;

        private List<string> _selectedInputFiles = new();
        private Dictionary<string, int> _detectedInputVersions = new();
        private string _currentOutputDirectory = "";
        private List<string> _lastConvertedFiles = new();

        private bool _debugEnabled;
        private string? _debugLogPath;

        private CancellationTokenSource? _conversionCts;
        private int _totalWorkers;
        private int _completedWorkers;
        private int _successfulConversions;
        private readonly List<string> _successfulOutputFiles = new();

        private bool _updateCheckInProgress;
        private bool _manualUpdateCheckActive;

        public MainWindow()
        {
            InitializeComponent();

            InputDropZoneControl.Clicked += BrowseInputFiles;
            InputDropZoneControl.FilesDropped += files => HandleInputFiles(files.ToList());

            UpdateTargetVersionOptions(new List<int>());

            _logger.Info("Application started");
            _logger.LogMemory("Application start");

            Loaded += (_, _) =>
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                timer.Tick += (sender, e) =>
                {
                    timer.Stop();
                    _ = CheckForUpdatesAsync(manual: false, force: false);
                };
                timer.Start();
            };
        }

        #region Custom title bar

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch (InvalidOperationException) { /* button was released mid-drag */ }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region File selection

        private void SelectFilesButton_Click(object sender, RoutedEventArgs e) => BrowseInputFiles();

        private void BrowseInputFiles()
        {
            _logger.LogFunctionCall("browse_input_files");

            string lastDir = _settings.GetLastDirectory("last_input_directory");
            var dialog = new OpenFileDialog
            {
                Title = "Select Input AEP Files",
                Multiselect = true,
                InitialDirectory = Directory.Exists(lastDir) ? lastDir : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Filter = "AEP Files (*.aep)|*.aep|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true && dialog.FileNames.Length > 0)
            {
                HandleInputFiles(dialog.FileNames.ToList());
            }
        }

        /// <summary>Store selected files, update version info, and refresh output location.
        /// Mirrors handle_input_files.</summary>
        private void HandleInputFiles(List<string> filePaths)
        {
            var normalizedPaths = new List<string>();
            var seenPaths = new HashSet<string>();

            foreach (string filePath in filePaths)
            {
                string pathStr;
                try
                {
                    pathStr = Path.GetFullPath(filePath);
                }
                catch
                {
                    continue;
                }

                if (seenPaths.Contains(pathStr)) continue;
                if (!File.Exists(pathStr)) continue;
                if (!pathStr.ToLowerInvariant().EndsWith(".aep")) continue;

                normalizedPaths.Add(pathStr);
                seenPaths.Add(pathStr);
            }

            if (normalizedPaths.Count == 0)
            {
                MessageBox.Show(this, "Please select one or more .aep files.", "Invalid Selection",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_debugEnabled)
            {
                _logger.Info($"Selected {normalizedPaths.Count} files");
                foreach (var fp in normalizedPaths) _logger.Debug($"Input file: {fp}");
            }

            _selectedInputFiles = normalizedPaths;
            _lastConvertedFiles = new List<string>();
            InputDropZoneControl.SetSelectedFiles(normalizedPaths);
            _settings.SetLastDirectory(normalizedPaths[0], "last_input_directory");

            UpdateDetectedVersions(normalizedPaths);
            UpdateOutputDirectoryFromInputs(normalizedPaths);
        }

        /// <summary>Detect versions across all selected input files and update UI controls.
        /// Mirrors _update_detected_versions.</summary>
        private void UpdateDetectedVersions(List<string> filePaths)
        {
            var detectedVersionLabels = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            var detectedVersionNumbers = new List<int>();
            int unknownCount = 0;
            _detectedInputVersions = new Dictionary<string, int>();

            foreach (string filePath in filePaths)
            {
                AeVersionDetection detection = AepConverter.DetectAeVersion(filePath);
                _detectedInputVersions[filePath] = detection.Version;

                if (detection.Version > 0)
                {
                    detectedVersionLabels.Add(detection.Version);
                    detectedVersionNumbers.Add(detection.Version);
                }
                else
                {
                    unknownCount++;
                }
            }

            if (detectedVersionLabels.Count > 0)
            {
                string joined = string.Join(", ", detectedVersionLabels.Select(v => $"AE {v}.x"));
                string unknownSuffix = unknownCount > 0 ? $" (+{unknownCount} unknown)" : "";
                DetectedVersionLabel.Text = $"Detected versions: {joined}{unknownSuffix}";
            }
            else
            {
                DetectedVersionLabel.Text = "Detected versions: Unknown";
            }

            UpdateTargetVersionOptions(detectedVersionNumbers);
        }

        /// <summary>Populate target version selector according to detected input versions.
        /// Mirrors update_target_version_options.</summary>
        private void UpdateTargetVersionOptions(List<int> detectedVersions)
        {
            TargetVersionCombo.Items.Clear();

            if (detectedVersions.Count == 0)
            {
                TargetVersionCombo.Items.Add(new TargetVersionOption("No target versions available"));
                TargetVersionCombo.SelectedIndex = 0;
                TargetVersionCombo.IsEnabled = false;
                return;
            }

            int maxTargetVersion = detectedVersions.Min() - 1;
            if (maxTargetVersion < MinAeVersion)
            {
                TargetVersionCombo.Items.Add(new TargetVersionOption("No lower versions available"));
                TargetVersionCombo.SelectedIndex = 0;
                TargetVersionCombo.IsEnabled = false;
                return;
            }

            for (int version = maxTargetVersion; version >= MinAeVersion; version--)
            {
                TargetVersionCombo.Items.Add(new TargetVersionOption(version, ExperimentalTargetVersions.Contains(version)));
            }

            TargetVersionCombo.SelectedIndex = 0;
            TargetVersionCombo.IsEnabled = true;
        }

        /// <summary>Set and display output directory info based on selected input files.
        /// Mirrors _update_output_directory_from_inputs.</summary>
        private void UpdateOutputDirectoryFromInputs(List<string> filePaths)
        {
            if (filePaths.Count == 0)
            {
                _currentOutputDirectory = "";
                OutputStatusLabel.Text = "Converted files will be saved next to selected input files.";
                return;
            }

            var outputDirs = filePaths.Select(fp => Path.GetDirectoryName(fp) ?? "").ToList();
            _currentOutputDirectory = outputDirs[0];
            _settings.SetLastDirectory(_currentOutputDirectory, "last_output_directory");

            var uniqueDirs = outputDirs.Distinct().OrderBy(d => d).ToList();
            if (uniqueDirs.Count == 1)
            {
                OutputStatusLabel.Text = $"Converted files will be saved to: {uniqueDirs[0]}";
            }
            else
            {
                OutputStatusLabel.Text =
                    "Converted files will be saved next to each original file.\n" +
                    $"Primary folder for quick access: {_currentOutputDirectory}";
            }
        }

        private void ViewOutputButton_Click(object sender, RoutedEventArgs e) => OpenConvertedFilesLocation();

        /// <summary>Open file manager at the latest converted files folder.
        /// Mirrors open_converted_files_location.</summary>
        private void OpenConvertedFilesLocation()
        {
            string targetDir;

            if (_lastConvertedFiles.Count > 0)
            {
                var outputDirs = _lastConvertedFiles
                    .Select(fp => Path.GetDirectoryName(fp) ?? "")
                    .Distinct().OrderBy(d => d).ToList();
                targetDir = outputDirs[0];
                if (outputDirs.Count > 1)
                {
                    UpdateLog($"Converted files are in multiple folders. Opening primary folder: {targetDir}");
                }
            }
            else if (!string.IsNullOrEmpty(_currentOutputDirectory))
            {
                targetDir = _currentOutputDirectory;
            }
            else
            {
                targetDir = _settings.GetLastDirectory("last_output_directory");
            }

            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                MessageBox.Show(this, "No converted files folder is available yet.", "No Output Folder",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(targetDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open folder:\n{targetDir}\n\n{ex.Message}", "Open Folder Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Conversion

        private async void ConvertButton_Click(object sender, RoutedEventArgs e) => await StartConversionAsync();

        /// <summary>Start the conversion process. Mirrors start_conversion.</summary>
        private async Task StartConversionAsync()
        {
            _logger.LogFunctionCall("start_conversion");
            _logger.LogMemory("Before conversion");

            if (_selectedInputFiles.Count == 0)
            {
                MessageBox.Show(this, "Please select at least one input .aep file", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var inputFiles = new List<string>(_selectedInputFiles);
            var unknownVersionFiles = inputFiles
                .Where(p => !_detectedInputVersions.TryGetValue(p, out int v) || v <= 0)
                .Select(Path.GetFileName)
                .ToList();

            if (unknownVersionFiles.Count > 0)
            {
                string preview = string.Join(", ", unknownVersionFiles.Take(3));
                string suffix = unknownVersionFiles.Count > 3 ? "..." : "";
                MessageBox.Show(this,
                    "Cannot convert because source version is unknown for:\n" +
                    $"{preview}{suffix}\n\n" +
                    "Please keep only files with detected versions.",
                    "Unknown Source Version", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!TargetVersionCombo.IsEnabled)
            {
                MessageBox.Show(this, "No compatible target versions are available.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TargetVersionCombo.SelectedItem is not TargetVersionOption selectedOption || selectedOption.IsPlaceholder)
            {
                MessageBox.Show(this, "Please select a target version", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int selectedTargetVersion = selectedOption.Version;

            if (ExperimentalTargetVersions.Contains(selectedTargetVersion))
            {
                var reply = MessageBox.Show(this,
                    $"AE {selectedTargetVersion}.x is marked as experimental.\n" +
                    "Compatibility is not guaranteed.\n\nContinue anyway?",
                    "Experimental Target Version", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (reply != MessageBoxResult.Yes) return;
            }

            string targetVersionLabel = $"AE {selectedTargetVersion}.x";

            // Disable UI during conversion
            ConvertButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            InputDropZoneControl.IsEnabled = false;
            ViewOutputButton.IsEnabled = false;

            ProgressBarControl.Value = 0;
            LogTextBox.Clear();
            _lastConvertedFiles = new List<string>();

            _conversionCts = new CancellationTokenSource();
            CancellationToken token = _conversionCts.Token;
            _successfulOutputFiles.Clear();

            var jobs = new List<(string InputFile, string OutputFile)>();
            foreach (string inputFile in inputFiles)
            {
                string currentOutputDir = Path.GetDirectoryName(inputFile) ?? "";
                string versionSuffix = targetVersionLabel.Replace(".", "").Replace(" ", ""); // "AE 24.x" -> "AE24x"
                string outputFilename = $"{Path.GetFileNameWithoutExtension(inputFile)}_{versionSuffix}.aep";
                string outputPath = Path.Combine(currentOutputDir, outputFilename);
                jobs.Add((inputFile, outputPath));
            }

            _totalWorkers = jobs.Count;
            _completedWorkers = 0;
            _successfulConversions = 0;

            if (_totalWorkers == 0)
            {
                MessageBox.Show(this, "No conversions to perform", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetUi();
                return;
            }

            var progress = new Progress<string>(UpdateLog);

            var tasks = jobs.Select(job => Task.Run(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return (Success: false, Message: "Cancelled", OutputPath: job.OutputFile);
                }

                if (_debugEnabled)
                {
                    _logger.LogFunctionCall("DowngradeWorker.run");
                    _logger.LogFileOperation("READ_START", job.InputFile);
                }

                var (success, message, _) = AepConverter.ConvertFile(job.InputFile, job.OutputFile, targetVersionLabel, progress);

                if (_debugEnabled)
                {
                    if (success) _logger.LogFileOperation("WRITE_START", job.OutputFile);
                    else _logger.Error($"Conversion failed: {message}");
                }

                return (Success: success, Message: message, OutputPath: job.OutputFile);
            }, token)).ToList();

            UpdateLog($"Started {_totalWorkers} conversion(s) for {inputFiles.Count} file(s)");

            foreach (var task in tasks)
            {
                try
                {
                    var result = await task;
                    SingleConversionFinished(result.Success, result.Message, result.OutputPath);
                }
                catch (Exception ex)
                {
                    SingleConversionFinished(false, ex.Message, "");
                }
            }
        }

        /// <summary>Handle completion of a single conversion. Mirrors single_conversion_finished.</summary>
        private void SingleConversionFinished(bool success, string message, string outputPath)
        {
            _completedWorkers++;

            if (success)
            {
                _successfulConversions++;
                _successfulOutputFiles.Add(outputPath);
            }

            if (_completedWorkers >= _totalWorkers)
            {
                AllConversionsFinished();
            }

            ProgressBarControl.Value = _totalWorkers > 0
                ? (double)_completedWorkers / _totalWorkers * 100.0
                : 0;
        }

        /// <summary>Handle completion of all conversions. Mirrors all_conversions_finished.</summary>
        private void AllConversionsFinished()
        {
            UpdateLog($"All conversions completed. {_successfulConversions}/{_totalWorkers} successful.");

            if (_successfulConversions > 0)
            {
                MessageBox.Show(this,
                    $"Conversion completed!\n{_successfulConversions}/{_totalWorkers} files converted successfully.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "All conversions failed!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _lastConvertedFiles = new List<string>(_successfulOutputFiles);
            if (_lastConvertedFiles.Count > 0)
            {
                _currentOutputDirectory = Path.GetDirectoryName(_lastConvertedFiles[0]) ?? "";
                _settings.SetLastDirectory(_currentOutputDirectory, "last_output_directory");
            }

            ResetUi();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelConversion();

        /// <summary>Cancel the conversion process. Mirrors cancel_conversion.
        /// Note: unlike the Python app's QThread.terminate() (a hard OS-level kill), this
        /// requests cooperative cancellation of not-yet-started jobs; a job already writing
        /// bytes to disk will still complete to avoid leaving a corrupt/partial output file.</summary>
        private void CancelConversion()
        {
            _conversionCts?.Cancel();
            ResetUi();
            UpdateLog("Conversion cancelled by user");
        }

        /// <summary>Update the log text area. Mirrors update_log.</summary>
        private void UpdateLog(string message)
        {
            void Append()
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();
                if (_debugEnabled) _logger.Info($"GUI: {message}");
            }

            if (Dispatcher.CheckAccess()) Append();
            else Dispatcher.Invoke(Append);
        }

        /// <summary>Reset UI to initial state. Mirrors reset_ui.</summary>
        private void ResetUi()
        {
            ConvertButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            InputDropZoneControl.IsEnabled = true;
            ViewOutputButton.IsEnabled = true;
        }

        #endregion

        #region Menu: File

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region Menu: Debug

        private void ToggleDebugMenuItem_Click(object sender, RoutedEventArgs e)
        {
            bool enable = ToggleDebugMenuItem.IsChecked;

            if (enable)
            {
                _debugLogPath = _logger.Enable();
                _debugEnabled = true;
                ToggleDebugMenuItem.Header = "Disable Debug Mode";
                UpdateLog("[DEBUG] Debug mode enabled");
                UpdateLog($"[DEBUG] Log file: {_debugLogPath}");
                _logger.Info("Debug mode enabled by user");
            }
            else
            {
                _logger.Info("Debug mode disabled by user");
                _logger.Disable();
                _debugEnabled = false;
                ToggleDebugMenuItem.Header = "Enable Debug Mode";
                UpdateLog("[DEBUG] Debug mode disabled");
                _debugLogPath = null;
            }
        }

        private void ViewDebugLogsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DebugLogViewerWindow(_logger, this);
            dialog.ShowDialog();
        }

        private void ExportDebugReportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Debug Report",
                FileName = $"debug_report_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                Filter = "Log Files (*.log)|*.log|Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == true)
            {
                string exportedPath = _logger.ExportLogs(dialog.FileName);
                MessageBox.Show(this, $"Debug report exported to:\n{exportedPath}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SystemInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SYSTEM INFORMATION");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();
            sb.AppendLine("PLATFORM");
            sb.AppendLine(new string('-', 30));
            foreach (var kv in PlatformInfo.GetPlatformInfo())
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("MEMORY");
            sb.AppendLine(new string('-', 30));
            foreach (var kv in MemoryInfo.GetMemoryInfo())
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }

            string infoText = sb.ToString();
            Clipboard.SetText(infoText);

            MessageBox.Show(this, $"System information:\n\n{infoText}\n\n(Copied to clipboard)", "System Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Menu: Help

        private void CheckForUpdatesMenuItem_Click(object sender, RoutedEventArgs e) =>
            _ = CheckForUpdatesAsync(manual: true, force: true);

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string aboutText =
                "AEP Downgrader\n\n" +
                $"Version {App.AppVersion}\n\n" +
                "Convert Adobe After Effects project files\n" +
                "from newer versions to older ones.\n\n" +
                "Debug Module: Available\n";

            MessageBox.Show(this, aboutText, "About AEP Downgrader", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Check GitHub releases for a newer app version. Mirrors check_for_updates.</summary>
        private async Task CheckForUpdatesAsync(bool manual, bool force)
        {
            if (_updateCheckInProgress)
            {
                if (manual)
                {
                    MessageBox.Show(this, "Update check is already in progress.", "Update Check",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (!force && !ShouldCheckUpdatesNow())
            {
                return;
            }

            _manualUpdateCheckActive = manual;
            _updateCheckInProgress = true;
            _settings.SetLong("updates/last_check_ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            try
            {
                ReleaseInfo release = await UpdateChecker.FetchLatestReleaseAsync();
                OnUpdateCheckFinished(release);
            }
            catch (Exception ex)
            {
                OnUpdateCheckError(ex.Message);
            }
            finally
            {
                _updateCheckInProgress = false;
            }
        }

        private bool ShouldCheckUpdatesNow()
        {
            long lastCheckTs = _settings.GetLong("updates/last_check_ts", 0);
            long nowTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return (nowTs - lastCheckTs) >= UpdateCheckIntervalSeconds;
        }

        private void OnUpdateCheckFinished(ReleaseInfo release)
        {
            bool manual = _manualUpdateCheckActive;
            _manualUpdateCheckActive = false;

            if (string.IsNullOrEmpty(release.TagName))
            {
                if (manual)
                {
                    MessageBox.Show(this, "Could not parse release version.", "Update Check",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (release.Draft || release.Prerelease)
            {
                if (manual)
                {
                    MessageBox.Show(this, "No stable update is currently available.", "Update Check",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            if (!UpdateChecker.IsNewerVersion(release.TagName, App.AppVersion))
            {
                if (manual)
                {
                    MessageBox.Show(this, $"You are already on the latest version ({App.AppVersion}).",
                        "You're Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            string skippedVersion = _settings.GetString("updates/skipped_version", "").Trim();
            if (!manual && skippedVersion == release.TagName)
            {
                return;
            }

            ShowUpdateAvailableDialog(release);
        }

        private void OnUpdateCheckError(string errorMessage)
        {
            bool manual = _manualUpdateCheckActive;
            _manualUpdateCheckActive = false;

            if (manual)
            {
                MessageBox.Show(this, $"Could not check for updates:\n{errorMessage}", "Update Check Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (_debugEnabled)
            {
                _logger.Warning($"Update check failed: {errorMessage}");
            }
        }

        /// <summary>Show update available dialog with download and skip actions.
        /// Mirrors _show_update_available_dialog.</summary>
        private void ShowUpdateAvailableDialog(ReleaseInfo release)
        {
            string releaseName = string.IsNullOrEmpty(release.Name) ? release.TagName : release.Name;
            string downloadUrl = string.IsNullOrEmpty(release.HtmlUrl)
                ? "https://github.com/itsAnchorpoint/AEP-Downgrader/releases/latest"
                : release.HtmlUrl;

            var dialog = new UpdateAvailableWindow(releaseName, App.AppVersion, release.TagName, release.Body)
            {
                Owner = this
            };
            dialog.ShowDialog();

            switch (dialog.Result)
            {
                case UpdateDialogResult.Download:
                    _settings.SetString("updates/skipped_version", "");
                    try
                    {
                        Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Could not open:\n{downloadUrl}\n\n{ex.Message}", "Open URL Failed",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;
                case UpdateDialogResult.SkipVersion:
                    _settings.SetString("updates/skipped_version", release.TagName);
                    break;
                case UpdateDialogResult.RemindLater:
                    break;
            }
        }

        #endregion
    }
}

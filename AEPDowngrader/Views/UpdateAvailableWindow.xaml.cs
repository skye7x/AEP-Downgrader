using System.Windows;

namespace AEPDowngrader.Views
{
    public enum UpdateDialogResult
    {
        Download,
        SkipVersion,
        RemindLater
    }

    /// <summary>
    /// Update-available dialog with Download / Skip this Version / Remind Later actions,
    /// mirroring AEPDowngraderGUI._show_update_available_dialog.
    /// </summary>
    public partial class UpdateAvailableWindow : Window
    {
        public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.RemindLater;

        public UpdateAvailableWindow(string releaseName, string currentVersion, string latestVersion, string releaseNotes)
        {
            InitializeComponent();
            TitleText.Text = $"A new version of AEP Downgrader is available: {releaseName}";
            InfoText.Text = $"Current version: {currentVersion}\nLatest version: {latestVersion}\n\nOpen download page?";

            if (!string.IsNullOrWhiteSpace(releaseNotes))
            {
                NotesExpander.Visibility = Visibility.Visible;
                NotesTextBox.Text = releaseNotes;
            }
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.Download;
            DialogResult = true;
        }

        private void SkipVersion_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.SkipVersion;
            DialogResult = true;
        }

        private void RemindLater_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.RemindLater;
            DialogResult = true;
        }
    }
}

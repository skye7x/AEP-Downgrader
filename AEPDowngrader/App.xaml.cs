using System.Windows;

namespace AEPDowngrader
{
    /// <summary>
    /// Application entry point. Mirrors the behavior of the Python `main()` function
    /// which created a QApplication, set application name/version, and showed the main window.
    /// </summary>
    public partial class App : Application
    {
        public const string AppVersion = "1.2.0";
        public const string AppName = "AEP Downgrader";
    }
}

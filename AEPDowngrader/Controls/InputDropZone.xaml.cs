using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AEPDowngrader.Controls
{
    /// <summary>
    /// Clickable drag-and-drop area for selecting .aep input files.
    /// Mirrors the InputDropZone QFrame subclass in AEPdowngrader.py.
    /// </summary>
    public partial class InputDropZone : UserControl
    {
        private bool _dragActive;

        /// <summary>Raised when one or more valid .aep files are dropped onto the zone.</summary>
        public event Action<IReadOnlyList<string>>? FilesDropped;

        /// <summary>Raised when the zone is clicked (used to open the browse dialog).</summary>
        public event Action? Clicked;

        public InputDropZone()
        {
            InitializeComponent();
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragLeave += OnDragLeave;
            Drop += OnDrop;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            IsEnabledChanged += (_, _) => UpdateStyle();
            UpdateStyle();
        }

        /// <summary>Update zone text based on selected files, mirroring set_selected_files.</summary>
        public void SetSelectedFiles(IReadOnlyList<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                DetailsLabel.Text = "No files selected";
                UpdateStyle();
                return;
            }

            if (filePaths.Count == 1)
            {
                DetailsLabel.Text = Path.GetFileName(filePaths[0]);
            }
            else
            {
                string preview = string.Join(", ", filePaths.Take(3).Select(Path.GetFileName));
                string suffix = filePaths.Count > 3 ? "..." : "";
                DetailsLabel.Text = $"{filePaths.Count} files: {preview}{suffix}";
            }
            UpdateStyle();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (ExtractValidAepPaths(e.Data).Count > 0)
            {
                _dragActive = true;
                UpdateStyle();
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            _dragActive = false;
            UpdateStyle();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            List<string> paths = ExtractValidAepPaths(e.Data);
            _dragActive = false;
            UpdateStyle();

            if (paths.Count > 0)
            {
                FilesDropped?.Invoke(paths);
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Clicked?.Invoke();
        }

        private static List<string> ExtractValidAepPaths(IDataObject data)
        {
            var paths = new List<string>();
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return paths;
            }

            if (data.GetData(DataFormats.FileDrop) is string[] droppedPaths)
            {
                foreach (string localPath in droppedPaths)
                {
                    if (File.Exists(localPath) && localPath.ToLowerInvariant().EndsWith(".aep"))
                    {
                        paths.Add(localPath);
                    }
                }
            }
            return paths;
        }

        private void UpdateStyle()
        {
            Brush border;
            Brush background;

            if (!IsEnabled)
            {
                border = (Brush)FindResource("DropZoneDisabledBorderBrush");
                background = (Brush)FindResource("DropZoneDisabledBackgroundBrush");
            }
            else if (_dragActive)
            {
                border = (Brush)FindResource("HighlightBrush");
                background = (Brush)FindResource("DropZoneActiveBackgroundBrush");
            }
            else
            {
                border = (Brush)FindResource("BorderBrush2");
                background = (Brush)FindResource("PanelBrush");
            }

            RootBorder.BorderBrush = border;
            RootBorder.Background = background;
        }
    }
}

namespace AEPDowngrader.Models
{
    /// <summary>
    /// A single entry in the target-version ComboBox, mirroring the (label, version, isExperimental)
    /// triple the Python app stored via addItem/setItemData on QComboBox.
    /// </summary>
    public class TargetVersionOption
    {
        public TargetVersionOption(int version, bool isExperimental)
        {
            Version = version;
            Label = $"AE {version}.x";
            IsExperimental = isExperimental;
        }

        /// <summary>Constructs a non-selectable placeholder entry, e.g. "No target versions available".</summary>
        public TargetVersionOption(string placeholderLabel)
        {
            Version = -1;
            Label = placeholderLabel;
            IsExperimental = false;
            IsPlaceholder = true;
        }

        public int Version { get; }
        public string Label { get; }
        public bool IsExperimental { get; }
        public bool IsPlaceholder { get; }
    }
}

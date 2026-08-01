using System;
using System.Collections.Generic;
using System.IO;

namespace AEPDowngrader.Services
{
    /// <summary>
    /// Result of a version detection scan of a .aep file header.
    /// </summary>
    public readonly struct AeVersionDetection
    {
        public AeVersionDetection(string description, int version)
        {
            Description = description;
            Version = version;
        }

        public string Description { get; }
        public int Version { get; }
    }

    /// <summary>
    /// Describes a single-byte patch to apply to the file content: at Offset, if the current
    /// byte equals FromValue, replace it with ToValue.
    /// </summary>
    public readonly struct ByteTransformation
    {
        public ByteTransformation(int offset, byte fromValue, byte toValue)
        {
            Offset = offset;
            FromValue = fromValue;
            ToValue = toValue;
        }

        public int Offset { get; }
        public byte FromValue { get; }
        public byte ToValue { get; }
    }

    /// <summary>
    /// Port of the binary .aep header analysis and patching logic found in
    /// AEPdowngrader.py (AEPDowngraderGUI.detect_ae_version and DowngradeWorker.run /
    /// get_target_signature / get_transformations / signature_to_version).
    ///
    /// The algorithm never fully parses the RIFX/Egg! container - it only inspects a
    /// fixed 20-byte "head" region starting at file offset 32, and conditionally
    /// patches a single byte (file offset 33) that encodes the major AE version.
    /// This class intentionally mirrors the Python code 1:1, including the same
    /// constants, offsets, and per-version heuristic tables, so behavior/output is
    /// byte-for-byte identical to the original application.
    /// </summary>
    public static class AepConverter
    {
        public const int MinAeVersion = 20;
        public const int MaxAeVersion = 33;
        public static readonly HashSet<int> ExperimentalTargetVersions = new() { 20, 21 };

        /// <summary>
        /// Detect the AE version of an .aep file based on header analysis.
        /// Mirrors AEPDowngraderGUI.detect_ae_version.
        /// </summary>
        public static AeVersionDetection DetectAeVersion(string filePath)
        {
            byte[] content;
            try
            {
                content = File.ReadAllBytes(filePath);
            }
            catch (Exception)
            {
                return new AeVersionDetection("Unknown version", 0);
            }

            if (content.Length < 52)
            {
                return new AeVersionDetection("Unknown (file too small)", 0);
            }

            // Extract head chunk data (20 bytes starting after the chunk header at offset 32)
            // head_data[1] == content[33]
            byte majorVersionByte = content[33];

            byte minVersionByte = (byte)(0x5b + (MinAeVersion - 20));
            byte maxVersionByte = (byte)(0x5b + (MaxAeVersion - 20));

            if (majorVersionByte >= minVersionByte && majorVersionByte <= maxVersionByte)
            {
                int version = majorVersionByte - 0x5b + 20;
                return new AeVersionDetection($"AE {version}.x (detected)", version);
            }

            return new AeVersionDetection("Unknown version", 0);
        }

        /// <summary>
        /// Get the target signature based on the target version - universal algorithm.
        /// Mirrors DowngradeWorker.get_target_signature. Returns null for an unparseable
        /// target version string (matching the Python None return).
        /// </summary>
        public static byte[]? GetTargetSignature(string targetVersion)
        {
            // Extract version number from string like "AE 24.x"
            int version;
            try
            {
                string[] tokens = targetVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string numberPart = tokens[1].Split('.')[0];
                version = int.Parse(numberPart);
            }
            catch (Exception)
            {
                return null;
            }

            // Universal pattern for head_data[1]: 0x5b + (version - 20)
            byte head1 = (byte)(0x5b + (version - 20));

            // head_data[3] heuristics
            byte head3;
            if (version <= 22) head3 = 0x2b;
            else if (version == 23) head3 = 0x09;
            else if (version == 24) head3 = 0x05;
            else if (version == 25) head3 = 0x09;
            else if (version == 26) head3 = 0x02;
            else head3 = 0x02; // Default to newer pattern

            // head_data[4]: 0x0b for older versions (22-23), 0x0f for newer (24+)
            byte head4 = (byte)(version >= 24 ? 0x0f : 0x0b);

            // head_data[5] heuristics
            byte head5;
            if (version == 22) head5 = 0x33;
            else if (version == 23) head5 = 0x3b;
            else if (version == 24) head5 = 0x02;
            else if (version == 25) head5 = 0x0b;
            else if (version == 26) head5 = 0x10;
            else head5 = 0x10; // Default to newer pattern

            // head_data[6]: 0x06 for most versions, 0x86 for AE 24
            byte head6 = (byte)(version == 24 ? 0x86 : 0x06);

            // head_data[7] heuristics
            byte head7;
            if (version == 22) head7 = 0x3b;
            else if (version == 23) head7 = 0x37;
            else if (version == 24) head7 = 0x34;
            else if (version == 25) head7 = 0x65;
            else if (version == 26) head7 = 0x43;
            else head7 = 0x43; // Default to newer pattern

            return new[] { head1, head3, head4, head5, head6, head7 };
        }

        /// <summary>
        /// Convert signature to version number using universal pattern detection.
        /// Mirrors DowngradeWorker.signature_to_version. Returns null when the signature
        /// byte falls outside the recognized AE 22-33 (0x5d-0x6a) range - note this
        /// intentionally uses a narrower range than DetectAeVersion, exactly like the
        /// original Python implementation.
        /// </summary>
        public static int? SignatureToVersion(byte[] sig)
        {
            if (sig.Length >= 1)
            {
                byte majorVersionByte = sig[0];
                if (majorVersionByte >= 0x5d && majorVersionByte <= 0x6a)
                {
                    return majorVersionByte - 0x5b + 20;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the list of transformations needed to convert from current to target signature.
        /// Mirrors DowngradeWorker.get_transformations.
        /// </summary>
        public static List<ByteTransformation> GetTransformations(byte[] currentSig, byte[] targetSig)
        {
            var transformations = new List<ByteTransformation>();

            int? currentVersion = SignatureToVersion(currentSig);
            int? targetVersion = SignatureToVersion(targetSig);

            if (currentVersion.HasValue && targetVersion.HasValue)
            {
                byte targetHead1 = (byte)(0x5b + (targetVersion.Value - 20));
                byte currentHead1 = currentSig[0];

                if (currentHead1 != targetHead1)
                {
                    const int offset = 32 + 1; // head_data[1] is at position 1 in head_data (file offset 33)
                    transformations.Add(new ByteTransformation(offset, currentHead1, targetHead1));
                }
            }

            return transformations;
        }

        /// <summary>
        /// Performs the full file conversion (read -> patch -> write), mirroring
        /// DowngradeWorker.run. Progress messages are reported through <paramref name="progress"/>
        /// exactly as the Python worker emitted them via progress_signal.
        /// </summary>
        /// <returns>(success, message, modificationCount)</returns>
        public static (bool Success, string Message, int Modifications) ConvertFile(
            string inputPath,
            string outputPath,
            string targetVersion,
            IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report($"Starting conversion to {targetVersion}...");

                byte[] content = File.ReadAllBytes(inputPath);

                progress?.Report("Analyzing file headers...");

                if (content.Length < 52)
                {
                    throw new InvalidOperationException("File too small to be a valid .aep file");
                }

                // Extract head chunk data (20 bytes starting after the chunk header)
                // head_data spans content[32..52)
                byte[] currentSig = new[]
                {
                    content[33], // head_data[1]
                    content[35], // head_data[3]
                    content[36], // head_data[4]
                    content[37], // head_data[5]
                    content[38], // head_data[6]
                    content[39], // head_data[7]
                };

                progress?.Report($"File signature: [{string.Join(", ", Array.ConvertAll(currentSig, b => $"0x{b:x2}"))}]");

                byte[]? targetSig = GetTargetSignature(targetVersion);
                if (targetSig == null)
                {
                    throw new InvalidOperationException($"Unsupported target version: {targetVersion}");
                }

                progress?.Report($"Target signature: [{string.Join(", ", Array.ConvertAll(targetSig, b => $"0x{b:x2}"))}]");

                List<ByteTransformation> transformations = GetTransformations(currentSig, targetSig);

                int modifications = 0;
                foreach (var t in transformations)
                {
                    if (t.Offset < content.Length && content[t.Offset] == t.FromValue)
                    {
                        content[t.Offset] = t.ToValue;
                        modifications++;
                    }
                }

                // Special handling for AE 22.x conversion - add warning
                if (targetVersion == "AE 22.x")
                {
                    progress?.Report("WARNING: Converting to AE 22.x may result in compatibility issues due to structural differences.");
                    progress?.Report("Consider using AE 23.x as target for better compatibility.");
                }

                progress?.Report($"Applied {modifications} modifications");
                progress?.Report("Writing converted file...");

                File.WriteAllBytes(outputPath, content);

                progress?.Report("Conversion completed successfully!");
                return (true, $"File converted successfully with {modifications} modifications", modifications);
            }
            catch (Exception e)
            {
                string errorMsg = $"Error during conversion: {e.Message}";
                progress?.Report(errorMsg);
                return (false, errorMsg, 0);
            }
        }
    }
}

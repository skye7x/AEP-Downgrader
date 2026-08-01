using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AEPDowngrader.Services
{
    /// <summary>
    /// Information about the latest GitHub release, mirroring the dict built by
    /// UpdateCheckWorker.run in the Python app.
    /// </summary>
    public class ReleaseInfo
    {
        public string TagName { get; set; } = "";
        public string Name { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
        public string Body { get; set; } = "";
        public string PublishedAt { get; set; } = "";
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
    }

    /// <summary>
    /// Background worker that fetches latest GitHub release info.
    /// Mirrors UpdateCheckWorker in AEPdowngrader.py.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ReleasesUrl = "https://api.github.com/repos/itsAnchorpoint/AEP-Downgrader/releases/latest";

        public static async Task<ReleaseInfo> FetchLatestReleaseAsync()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AEP-Downgrader-UpdateCheck");

            HttpResponseMessage response = await client.GetAsync(ReleasesUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            string payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;

            string GetStr(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                    ? (el.GetString() ?? "").Trim()
                    : "";

            return new ReleaseInfo
            {
                TagName = GetStr("tag_name"),
                Name = GetStr("name"),
                HtmlUrl = GetStr("html_url"),
                Body = root.TryGetProperty("body", out var bodyEl) ? (bodyEl.GetString() ?? "") : "",
                PublishedAt = GetStr("published_at"),
                Draft = root.TryGetProperty("draft", out var draftEl) && draftEl.ValueKind == JsonValueKind.True,
                Prerelease = root.TryGetProperty("prerelease", out var preEl) && preEl.ValueKind == JsonValueKind.True,
            };
        }

        /// <summary>Parse semantic-like version text into an array for comparisons.</summary>
        public static int[] NormalizeVersionTuple(string? versionString)
        {
            if (string.IsNullOrEmpty(versionString)) return Array.Empty<int>();
            var matches = Regex.Matches(versionString, @"\d+");
            var result = new int[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                result[i] = int.Parse(matches[i].Value);
            }
            return result;
        }

        /// <summary>Compare two versions and return true if candidate > current.</summary>
        public static bool IsNewerVersion(string? candidateVersion, string? currentVersion)
        {
            int[] candidate = NormalizeVersionTuple(candidateVersion);
            int[] current = NormalizeVersionTuple(currentVersion);
            if (candidate.Length == 0 || current.Length == 0) return false;

            int maxLen = Math.Max(candidate.Length, current.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int c = i < candidate.Length ? candidate[i] : 0;
                int cur = i < current.Length ? current[i] : 0;
                if (c != cur) return c > cur;
            }
            return false;
        }
    }
}

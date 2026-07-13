using GI_Subtitles.Models;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GI_Subtitles.Services.Update
{
    public static class UpdateChecker
    {
        public const string DefaultManifestUrl = "https://2langs.com/data/release-manifest.json";
        public const string WindowsMsiAsset = "windows-x64-msi";

        public static ReleaseManifest ParseManifest(string json)
        {
            var manifest = JsonConvert.DeserializeObject<ReleaseManifest>(json);
            if (manifest == null || manifest.SchemaVersion != 1 ||
                !TryParseVersion(manifest.Version, out _) || manifest.Assets == null ||
                !manifest.Assets.TryGetValue(WindowsMsiAsset, out var asset) ||
                asset == null || !Uri.TryCreate(asset.Url, UriKind.Absolute, out _) ||
                string.IsNullOrWhiteSpace(asset.Sha256) || asset.Size <= 0)
            {
                return null;
            }

            return manifest;
        }

        public static bool ShouldOfferUpdate(
            ReleaseManifest manifest,
            string currentVersion,
            string ignoredVersion,
            string installationId)
        {
            if (manifest == null || !TryParseVersion(currentVersion, out var current) ||
                !TryParseVersion(manifest.Version, out var available) || available <= current ||
                string.Equals(NormalizeVersion(ignoredVersion), NormalizeVersion(manifest.Version), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (manifest.Rollout <= 0)
            {
                return false;
            }

            if (manifest.Rollout >= 100)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(installationId))
            {
                return false;
            }

            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(installationId + ":" + NormalizeVersion(manifest.Version));
                var hash = sha256.ComputeHash(bytes);
                var bucket = ((hash[0] << 8) | hash[1]) % 100;
                return bucket < manifest.Rollout;
            }
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            return Version.TryParse(NormalizeVersion(value), out version);
        }

        private static string NormalizeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().TrimStart('v', 'V');
            var suffix = normalized.IndexOf('-');
            return suffix >= 0 ? normalized.Substring(0, suffix) : normalized;
        }
    }
}

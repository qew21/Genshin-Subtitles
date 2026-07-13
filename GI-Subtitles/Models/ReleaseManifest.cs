using Newtonsoft.Json;
using System.Collections.Generic;

namespace GI_Subtitles.Models
{
    public class ReleaseManifest
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("publishedAt")]
        public string PublishedAt { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("rollout")]
        public int Rollout { get; set; }

        [JsonProperty("minimumVersion")]
        public string MinimumVersion { get; set; }

        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }

        [JsonProperty("assets")]
        public Dictionary<string, ReleaseAsset> Assets { get; set; }
    }

    public class ReleaseAsset
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }
}

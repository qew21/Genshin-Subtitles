using System.Collections.Generic;

namespace GI_Subtitles.Models
{
    /// <summary>
    /// Minimal metadata for game selection list
    /// </summary>
    public class GameMetadata
    {
        public string Name { get; set; } // Internal ID used for config filename
        public string DisplayResourceKey { get; set; } // Optional: Key for Strings.xaml
        
        /// <summary>
        /// Localized names for the UI (keys: zh-CN, en-US, ja-JP)
        /// </summary>
        public Dictionary<string, string> DisplayNames { get; set; } = new Dictionary<string, string>();
    }
}

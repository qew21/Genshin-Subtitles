using System.Collections.Generic;

namespace GI_Subtitles.Models
{
    /// <summary>
    /// Configuration for a specific game (technical details only)
    /// </summary>
    public class GameConfig
    {
        public string RepoUrl { get; set; }
        public string RepoType { get; set; }
        public string InputUrlTemplate { get; set; }
        public string OutputUrlTemplate { get; set; }
        public string MediumUrlTemplate { get; set; }
        public string TestFile { get; set; }
        public string Warning { get; set; }
        
        /// <summary>
        /// Mapping from internal language codes (CHS, EN, JP) to game-specific URL parts
        /// </summary>
        public Dictionary<string, string> LanguageMapping { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets the download URL for a specific language
        /// </summary>
        public string GetDownloadUrl(string language, bool isInput = true)
        {
            string template = isInput ? InputUrlTemplate : OutputUrlTemplate;
            return FillTemplate(template, language);
        }

        public string GetMediumDownloadUrl(string language)
        {
            return FillTemplate(MediumUrlTemplate, language);
        }

        private string FillTemplate(string template, string language)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            string mappedLang = language;
            if (LanguageMapping != null && LanguageMapping.ContainsKey(language))
            {
                mappedLang = LanguageMapping[language];
            }

            // Replace common placeholders with mapped language
            return template.Replace("{Language}", mappedLang)
                           .Replace("{LanguageLower}", mappedLang.ToLower())
                           .Replace("{LanguageUpper}", mappedLang.ToUpper());
        }
    }
}

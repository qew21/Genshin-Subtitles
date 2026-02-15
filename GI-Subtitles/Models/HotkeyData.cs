namespace GI_Subtitles.Models
{
    /// <summary>
    /// Hotkey data model
    /// </summary>
    public class HotkeyData
    {
        public int Id { get; set; }
        public bool IsCtrl { get; set; }
        public bool IsShift { get; set; }
        public char SelectedKey { get; set; }
        public string Description { get; set; }
    }
}


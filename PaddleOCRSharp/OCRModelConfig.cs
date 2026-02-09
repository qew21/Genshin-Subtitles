using System.IO;

namespace PaddleOCRSharp
{
    /// <summary>
    /// OCR model configuration
    /// </summary>
    public class OCRModelConfig
    {
        /// <summary>
        /// Detection model path
        /// </summary>
        public string det_infer { get; set; }

        /// <summary>
        /// Classification model path
        /// </summary>
        public string cls_infer { get; set; }

        /// <summary>
        /// Recognition model path
        /// </summary>
        public string rec_infer { get; set; }

        /// <summary>
        /// Character dictionary path
        /// </summary>
        public string keys { get; set; }

        public OCRModelConfig()
        {
            var root = GetRootDirectory();
            var modelPathRoot = Path.Combine(root, "inference");
            det_infer = Path.Combine(modelPathRoot, "Det", "V5", "PP-OCRv5_mobile_det_infer", "slim.onnx");
            cls_infer = Path.Combine(modelPathRoot, "ch_ppocr_mobile_v2.0_cls_infer"); // Optional, not used
            rec_infer = Path.Combine(modelPathRoot, "Rec", "V5", "PP-OCRv5_mobile_rec_infer", "slim.onnx");
            keys = Path.Combine(modelPathRoot, "ppocr_keys.txt"); // Optional, character dictionary from inference.yml
        }

        /// <summary>
        /// Get root directory
        /// </summary>
        private static string GetRootDirectory()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(exePath);
        }
    }
}
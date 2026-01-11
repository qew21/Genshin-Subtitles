using System.IO;

namespace PaddleOCRSharp
{
    /// <summary>
    /// OCR模型配置
    /// </summary>
    public class OCRModelConfig
    {
        /// <summary>
        /// 检测模型路径
        /// </summary>
        public string det_infer { get; set; }

        /// <summary>
        /// 分类模型路径
        /// </summary>
        public string cls_infer { get; set; }

        /// <summary>
        /// 识别模型路径
        /// </summary>
        public string rec_infer { get; set; }

        /// <summary>
        /// 字符字典路径
        /// </summary>
        public string keys { get; set; }

        public OCRModelConfig()
        {
            var root = GetRootDirectory();
            var modelPathRoot = Path.Combine(root, "inference");
            det_infer = Path.Combine(modelPathRoot, "Det", "V5", "PP-OCRv5_mobile_det_infer", "slim.onnx");
            cls_infer = Path.Combine(modelPathRoot, "ch_ppocr_mobile_v2.0_cls_infer"); // 可选，不使用
            rec_infer = Path.Combine(modelPathRoot, "Rec", "V5", "PP-OCRv5_mobile_rec_infer", "slim.onnx");
            keys = Path.Combine(modelPathRoot, "ppocr_keys.txt"); // 可选，字符字典从inference.yml读取
        }

        /// <summary>
        /// 获取根目录
        /// </summary>
        private static string GetRootDirectory()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(exePath);
        }
    }
}
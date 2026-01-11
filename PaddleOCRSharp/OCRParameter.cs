namespace PaddleOCRSharp
{
    /// <summary>
    /// OCR识别参数
    /// </summary>
    public class OCRParameter
    {
        /// <summary>
        /// 是否使用GPU
        /// </summary>
        public bool use_gpu { get; set; } = false;

        /// <summary>
        /// GPU设备ID
        /// </summary>
        public int gpu_id { get; set; } = 0;

        /// <summary>
        /// GPU内存大小
        /// </summary>
        public int gpu_mem { get; set; } = 4000;

        /// <summary>
        /// CPU数学库线程数
        /// </summary>
        public int cpu_math_library_num_threads { get; set; } = 3;

        /// <summary>
        /// 是否启用MKLDNN
        /// </summary>
        public bool enable_mkldnn { get; set; } = true;

        /// <summary>
        /// 最大边长
        /// </summary>
        public int max_side_len { get; set; } = 960;

        /// <summary>
        /// 检测模型DB阈值
        /// </summary>
        public float det_db_thresh { get; set; } = 0.3f;

        /// <summary>
        /// 检测模型DB框阈值
        /// </summary>
        public float det_db_box_thresh { get; set; } = 0.5f;

        /// <summary>
        /// 检测模型DB扩展比例
        /// </summary>
        public float det_db_unclip_ratio { get; set; } = 1.6f;

        /// <summary>
        /// 是否使用膨胀
        /// </summary>
        public bool use_dilation { get; set; } = false;

        /// <summary>
        /// 检测模型DB得分模式
        /// </summary>
        public bool det_db_score_mode { get; set; } = true;

        /// <summary>
        /// 是否可视化
        /// </summary>
        public bool visualize { get; set; } = false;

        /// <summary>
        /// 是否使用角度分类器
        /// </summary>
        public bool use_angle_cls { get; set; } = false;

        /// <summary>
        /// 分类器阈值
        /// </summary>
        public float cls_thresh { get; set; } = 0.9f;

        /// <summary>
        /// 分类器批处理大小
        /// </summary>
        public int cls_batch_num { get; set; } = 1;

        /// <summary>
        /// 识别模型批处理大小
        /// </summary>
        public int rec_batch_num { get; set; } = 6;

        /// <summary>
        /// 识别模型图像高度
        /// </summary>
        public int rec_img_h { get; set; } = 48;

        /// <summary>
        /// 识别模型图像宽度
        /// </summary>
        public int rec_img_w { get; set; } = 320;

        /// <summary>
        /// 是否显示图像可视化结果
        /// </summary>
        public bool show_img_vis { get; set; } = false;

        /// <summary>
        /// 是否使用TensorRT
        /// </summary>
        public bool use_tensorrt { get; set; } = false;
    }
}
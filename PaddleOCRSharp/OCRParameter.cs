namespace PaddleOCRSharp
{
    /// <summary>
    /// OCR recognition parameters
    /// </summary>
    public class OCRParameter
    {
        /// <summary>
        /// Whether to use GPU
        /// </summary>
        public bool use_gpu { get; set; } = false;

        /// <summary>
        /// GPU device ID
        /// </summary>
        public int gpu_id { get; set; } = 0;

        /// <summary>
        /// GPU memory size
        /// </summary>
        public int gpu_mem { get; set; } = 4000;

        /// <summary>
        /// CPU math library thread count
        /// </summary>
        public int cpu_math_library_num_threads { get; set; } = 3;

        /// <summary>
        /// Whether to enable MKLDNN
        /// </summary>
        public bool enable_mkldnn { get; set; } = true;

        /// <summary>
        /// Maximum side length
        /// </summary>
        public int max_side_len { get; set; } = 960;

        /// <summary>
        /// Detection model DB threshold
        /// </summary>
        public float det_db_thresh { get; set; } = 0.3f;

        /// <summary>
        /// Detection model DB box threshold
        /// </summary>
        public float det_db_box_thresh { get; set; } = 0.5f;

        /// <summary>
        /// Detection model DB expansion ratio
        /// </summary>
        public float det_db_unclip_ratio { get; set; } = 1.6f;

        /// <summary>
        /// Whether to use dilation
        /// </summary>
        public bool use_dilation { get; set; } = false;

        /// <summary>
        /// Detection model DB score mode
        /// </summary>
        public bool det_db_score_mode { get; set; } = true;

        /// <summary>
        /// Whether to visualize
        /// </summary>
        public bool visualize { get; set; } = false;

        /// <summary>
        /// Whether to use angle classifier
        /// </summary>
        public bool use_angle_cls { get; set; } = false;

        /// <summary>
        /// Classifier threshold
        /// </summary>
        public float cls_thresh { get; set; } = 0.9f;

        /// <summary>
        /// Classifier batch size
        /// </summary>
        public int cls_batch_num { get; set; } = 1;

        /// <summary>
        /// Recognition model batch size
        /// </summary>
        public int rec_batch_num { get; set; } = 6;

        /// <summary>
        /// Recognition model image height
        /// </summary>
        public int rec_img_h { get; set; } = 48;

        /// <summary>
        /// Recognition model image width
        /// </summary>
        public int rec_img_w { get; set; } = 320;

        /// <summary>
        /// Whether to display image visualization result
        /// </summary>
        public bool show_img_vis { get; set; } = false;

        /// <summary>
        /// Whether to use TensorRT
        /// </summary>
        public bool use_tensorrt { get; set; } = false;
    }
}
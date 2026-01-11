namespace PaddleOCRSharp
{
    /// <summary>
    /// 日志记录器类，使用 log4net
    /// </summary>
    public static class Logger
    {
        public static log4net.ILog Log = log4net.LogManager.GetLogger("LogFileAppender");
    }
}


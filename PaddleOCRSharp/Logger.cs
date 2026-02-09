namespace PaddleOCRSharp
{
    /// <summary>
    /// Logger class, using log4net
    /// </summary>
    public static class Logger
    {
        public static log4net.ILog Log = log4net.LogManager.GetLogger("LogFileAppender");
    }
}


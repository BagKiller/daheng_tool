using NLog;

namespace Client.Util
{
    public class testLogger
    {
        private static ILogger logger = LogManager.GetCurrentClassLogger();

        public static void Info(string info, Exception ex = null)
        {
            logger.Info(ex, $"Info:{info}");
        }

        public static void Error(string errorInfo, Exception ex = null)
        {
            logger.Error(ex, $"ErrorInfo:{errorInfo}\nMessage:{ex?.Message}\nStackTrace:{ex?.StackTrace}");
        }

        public static void Debug(string debugInfo, Exception ex = null)
        {
            logger.Debug(ex, $"DebugInfo:{debugInfo}\nMessage:{ex?.Message}\nStackTrace:{ex?.StackTrace}");
        }
        public static void Warn(string warnInfo, Exception ex = null)
        {
            logger.Warn(ex, $"WarnInfo:{warnInfo}");
        }

    }

    public class Logger<T> where T : class
    {
        private ILogger logger = LogManager.GetLogger(typeof(T).FullName);

        public void LogInfo(string info, Exception ex = null)
        {
            logger.Info(ex, $"Info:{info}\nMessage:{ex?.Message}\nStackTrace:{ex?.StackTrace}");
        }

        public void LogError(string errorInfo, Exception ex = null)
        {
            logger.Error(ex, $"ErrorInfo:{errorInfo}\nMessage:{ex?.Message}\nStackTrace:{ex?.StackTrace}");
        }

        public void LogDebug(string debugInfo, Exception ex = null)
        {
            logger.Debug(ex, $"DebugInfo:{debugInfo}\nMessage:{ex?.Message}\nStackTrace:{ex?.StackTrace}");
        }
    }
}
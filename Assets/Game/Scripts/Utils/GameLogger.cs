using UnityEngine;
using System.IO;

namespace Game.Scripts.Utils
{
    public class GameLogger : ILogHandler
    {
        private ILogHandler m_DefaultLogHandler = Debug.unityLogger.logHandler;

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            m_DefaultLogHandler.LogFormat(logType, context, $"[{Time.time:F2}] {format}", args);
        }

        public void LogException(System.Exception exception, Object context)
        {
            m_DefaultLogHandler.LogException(exception, context);
        }
    }
}

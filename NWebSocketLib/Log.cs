using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WebSocketServer;

namespace NWebSocketLib
{
    public class Log
    {
        public string LogMessage { get; set; }
        public ServerLogLevel ServerLogLevel { get; set; }

        public Log(string logMessage, ServerLogLevel serverLogLevel)
        {
            this.LogMessage = logMessage;
            this.ServerLogLevel = serverLogLevel;
        }
    }
}

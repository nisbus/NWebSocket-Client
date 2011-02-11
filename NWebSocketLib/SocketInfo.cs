using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NWebSocketLib
{
    public class SocketInfo
    {
        public string Message { get; private set; }
        public SocketInfoCode Code { get; private set; }

        public SocketInfo(SocketInfoCode code, string str)
        {
            this.Code = code;
            this.Message = str;
        }
    }

    public enum SocketInfoCode
    {
        Ok,
        Error,
        Closed,
        Sent,
        Connected,
        Received
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NWebSocketLib
{
    public class ClientInfo
    {
        public SocketInfoCode SocketInfoCode { get; set; }
        public string Message { get; set; }

        public ClientInfo(SocketInfoCode socketInfoCode, string message)
        {
            this.SocketInfoCode = socketInfoCode;
            this.Message = message;
        }
    }
}

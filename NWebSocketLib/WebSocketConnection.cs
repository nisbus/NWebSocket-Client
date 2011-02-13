using System;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace NWebSocketLib
{
    /// <summary>
    /// This class was originally downloaded from http://nugget.codeplex.com/
    /// 
    /// Copyright (c) 2010 
    ///Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
    ///The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
    /// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
    /// </summary>
    public class WebSocketConnection : IDisposable
    {
        #region Private members

        private int bufferSize;                                         // the size of the buffer
        private byte[] dataBuffer;                                      // buffer to hold the data we are reading
        private StringBuilder dataString;                               // holds the currently accumulated data
        private enum WrapperBytes : byte { Start = 0, End = 255 };      // data passed between client and server are wrapped in start and end bytes according to the protocol (0x00, 0xFF)
        Subject<byte> incomingStream = new Subject<byte>();             // internal event to frame the incoming data
        Subject<string> onMessage = new Subject<string>();            // a backing subject for the OnMessage subscription
        Subject<SocketInfo> onSocketEvent = new Subject<SocketInfo>();  // a backing subject for socket events, used for debugging and logging
        private bool isClosed;
        /// <summary>
        /// Gets the socket used for the connection
        /// </summary>
        #endregion

        #region Properties

        public Socket Socket { get; private set; }

        ///<summary>
        /// Subscription for incoming messages
        /// </summary>
        public IQbservable<string> OnMessage
        {
            get
            {
                return onMessage.AsQbservable();
            }
        }

        /// <summary>
        /// Subscription for SocketInfo events (Debug and logging)
        /// </summary>
        public IQbservable<SocketInfo> OnSocketInfo
        { 
            get 
            { 
                return onSocketEvent.AsQbservable(); 
            } 
        }

        #endregion

        #region Constructors

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="connection">The socket on which to esablish the connection</param>
        /// <param name="webSocketOrigin">The origin from which the server is willing to accept connections, usually this is your web server. For example: http://localhost:8080.</param>
        /// <param name="webSocketLocation">The location of the web socket server (the server on which this code is running). For example: ws://localhost:8181/service. The '/service'-part is important it could be '/somethingelse' but it needs to be there.</param>
        public WebSocketConnection(Socket socket)
            : this(socket, 255) { }

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="connection">The socket on which to esablish the connection</param>
        /// <param name="webSocketOrigin">The origin from which the server is willing to accept connections, usually this is your web server. For example: http://localhost:8080.</param>
        /// <param name="webSocketLocation">The location of the web socket server (the server on which this code is running). For example: ws://localhost:8181/service. The '/service'-part is important it could be '/somethingelse' but it needs to be there.</param>
        /// <param name="bufferSize">The size of the buffer used to receive data</param>
        /// <param name="messageConversionFunc">A function for converting the string message to for example a .NET object</param>
        public WebSocketConnection(Socket socket, int bufferSize)
        {
            this.bufferSize = bufferSize;
            Socket = socket;
            dataBuffer = new byte[bufferSize];
            dataString = new StringBuilder();
           
            //This is where the framing takes place
            incomingStream.Subscribe(x =>
            {
                if (x == (byte)WrapperBytes.End)
                {
                    onMessage.OnNext(dataString.ToString());
                    dataString = null;
                    dataString = new StringBuilder();
                }
                else if (x != (byte)WrapperBytes.Start)
                {
                    dataString.Append(Encoding.UTF8.GetString(new byte[1] { x }, 0, 1));
                }
            }, (ex) =>//The subscription received an exception and forwards it to the message listener
            {
                onMessage.OnError(ex);
            }, () =>//The subscription has completed i.e. the connection is closed.
            {
                onMessage.OnCompleted();
            });

            Listen();
        }

        #endregion

        #region API

        /// <summary>
        /// Send a string to the client
        /// </summary>
        /// <param name="str">the string to send to the client</param>
        public void Send(string str)
        {
            if (Socket.Connected)
            {
                try
                {
                    // start with a 0x00
                    Socket.Send(new byte[] { (byte)WrapperBytes.Start }, 1, 0);
                    // send the string
                    Socket.Send(Encoding.UTF8.GetBytes(str));
                    // end with a 0xFF
                    Socket.Send(new byte[] { (byte)WrapperBytes.End }, 1, 0);

                    //This is where the client can monitor all sent messages, for debugging or logging
                    //onSocketEvent.OnNext(new SocketInfo(SocketInfoCode.Sent, str));
                }
                catch (Exception ex)
                {
                    onMessage.OnError(ex);
                }
            }
        }

        /// <summary>
        /// Closes the socket
        /// </summary>
        public void Close()
        {
            isClosed = true;
            Socket.Close();
            onSocketEvent.OnNext(new SocketInfo(SocketInfoCode.Closed, "Bye bye"));
            onMessage.OnCompleted();
        }

        #endregion

        #region internal methods

        /// <summary>
        /// Listens for incomming data
        /// </summary>
        private void Listen()
        {
            try
            {
                Socket.BeginReceive(dataBuffer, 0, dataBuffer.Length, 0, Read, null);
            }
            catch (SocketException ex)
            {
                onMessage.OnError(ex);
                Close();
            }
        }

        /// <summary>
        /// reads the incomming data and triggers the incomingData subscription event for each byte received
        /// </summary>
        private void Read(IAsyncResult ar)
        {
            if (isClosed)
            {
                return;
            }

            int sizeOfReceivedData = -1;
            try
            {
                sizeOfReceivedData = Socket.EndReceive(ar);
            }
            catch (SocketException se)
            {
                onMessage.OnError(se);
                return;
            }
            if (sizeOfReceivedData > 0)
            {
                foreach (var c in dataBuffer)
                    incomingStream.OnNext(c);
                dataBuffer = new byte[bufferSize];
                // continue listening for more data
                Listen();
            }
            else // the socket is closed
            {
                onMessage.OnCompleted();
            }
        }



        /// <summary>
        /// Closes the socket
        /// </summary>
        public void Dispose()
        {
            Close();
            Socket = null;
        }

        #endregion
    }
}

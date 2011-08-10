using System;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

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
        const string HEARTBEAT = "~h~";
        const string MESSAGE_LENGTH_DELIMITER = "~m~";

        ConcurrentQueue<string> writeQueue = new ConcurrentQueue<string>();
        CancellationTokenSource cancelSource = new CancellationTokenSource();

        private int bufferSize;                                         // the size of the buffer
        private byte[] dataBuffer;                                      // buffer to hold the data we are reading
        private enum WrapperBytes : byte { Start = 0, End = 255 };      // data passed between client and server are wrapped in start and end bytes according to the protocol (0x00, 0xFF)
        private bool isConnected;      
        private bool isSocketIO;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the socket used for the connection
        /// </summary>
        public Stream Stream { get; set; }
        public bool IsConnected { get { return isConnected; } }
        public event Action<string> OnMessage;
        public event Action<Exception> OnError;
        
        #endregion

        #region Constructors

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="connection">The socket on which to esablish the connection</param>
        /// <param name="bufferSize">The size of the buffer used to receive data</param>
        public WebSocketConnection(Stream stream, int bufferSize)
        {
            this.bufferSize = bufferSize;
            Stream = stream;            
            dataBuffer = new byte[bufferSize];            
            //Start the writer thread
            StartWriterThread();
            isConnected = true;
        }

        private void StartWriterThread()
        {
            new TaskFactory().StartNew(() =>
            {
                while (!cancelSource.IsCancellationRequested)
                {
                    if (writeQueue.Count == 0)
                    {
                        lock (writeQueue)
                        {
                            Monitor.Wait(writeQueue);
                            continue;
                        }
                    }
                    if (!cancelSource.IsCancellationRequested)
                    {
                        string message;
                        writeQueue.TryDequeue(out message);
                        try
                        {
                            byte[] msg = EncodeSendMessage(message);
                            Stream.Write(msg, 0, msg.Length);
                        }
                        catch (Exception ex)
                        {
                            if (OnError != null && !cancelSource.IsCancellationRequested)
                                OnError(ex);

                            //throw;
                        }
                    }
                }
            }, cancelSource.Token);
        }

        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="connection">The socket on which to esablish the connection</param>
        /// <param name="webSocketOrigin">The origin from which the server is willing to accept connections, usually this is your web server. For example: http://localhost:8080.</param>
        /// <param name="webSocketLocation">The location of the web socket server (the server on which this code is running). For example: ws://localhost:8181/service. The '/service'-part is important it could be '/somethingelse' but it needs to be there.</param>
        public WebSocketConnection(Stream stream)
            : this(stream, 255) { }

        public WebSocketConnection(Stream stream, bool socketIo)
            : this(stream, 255)
        {
            this.isSocketIO = socketIo;
        }

        public WebSocketConnection(Stream stream, int bufferSize, bool socketIo)
            : this(stream, bufferSize)
        {
            this.isSocketIO = socketIo;
        }

        #endregion

        #region API

        /// <summary>
        /// Send a string to the client
        /// </summary>
        /// <param name="str">the string to send to the client</param>
        public void Send(string str)
        {
            try
            {
                //Enqueue the message to be sent
                lock (writeQueue)
                {
                    writeQueue.Enqueue(str);
                    Monitor.Pulse(writeQueue);
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
                Close();
            }
        }

        private byte[] EncodeSendMessage(string str)
        {
            // start with a 0x00
            List<byte> bufferToSend = new List<byte> { (byte)WrapperBytes.Start };

            // Encode the string
            if (isSocketIO)
            {
                var msg = Encoding.UTF8.GetBytes(EncodeMessage(str));
                bufferToSend.AddRange(msg);
            }
            else
            {
                var msg = Encoding.UTF8.GetBytes(str);
                bufferToSend.AddRange(msg);
            }
            // end with a 0xFF
            bufferToSend.Add((byte)WrapperBytes.End);
            return bufferToSend.ToArray();
        }

        /// <summary>
        /// Closes the socket
        /// </summary>
        public void Close()
        {
            cancelSource.Cancel();
            isConnected = false;
            Stream.Close();
        }

        #endregion

        #region internal methods

        List<byte> databuffer = new List<byte>();


        bool receivedGuid = false;
        string sessionKey = string.Empty;
        private void Frame(byte x)
        {
            try
            {
                if (x == (byte)WrapperBytes.End)
                {
                    string msg = Encoding.UTF8.GetString(databuffer.ToArray(), 0, databuffer.Count);                    

                    if (receivedGuid == false)
                    {
                        sessionKey = DecodeMessage(msg);
                        receivedGuid = true;
                    }
                    else
                    {
                        if (isSocketIO)
                        {
                            if (IsHeartbeat(msg))
                                SendHeartBeat(msg);
                            else
                            {
                                if (OnMessage != null)
                                    OnMessage(DecodeMessage(msg));
                            }
                        }
                        else
                        {
                            if (OnMessage != null)
                                OnMessage(DecodeMessage(msg));
                        }
                    }

                    databuffer.Clear();
                }
                else if (x != (byte)WrapperBytes.Start)
                {
                    databuffer.Add(x);
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
                throw;
            }
        }

        private string EncodeMessage(string msg)
        {
            return MESSAGE_LENGTH_DELIMITER + msg.Length + MESSAGE_LENGTH_DELIMITER + msg;
        }

        private string DecodeMessage(string msg)
        {
            return msg.Substring(msg.LastIndexOf(MESSAGE_LENGTH_DELIMITER) + 3);
        }

        private bool IsHeartbeat(string msg)
        {
            return msg.Contains(HEARTBEAT);
        }      

        /// <summary>
        /// Listens for incomming data
        /// </summary>
        public void Listen()
        {

            try
            {
                var async = Stream.BeginRead(dataBuffer, 0, dataBuffer.Length, Read, null);
                async.AsyncWaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                if (OnError != null)
                {
                    OnError(ex);
                    //OnError(new Exception("Unable to read from stream"));
                }
                Close();
            }
        }

        /// <summary>
        /// reads the incomming data and triggers the incomingData subscription event for each byte received
        /// </summary>
        private void Read(IAsyncResult ar)
        {
            if (!isConnected)
            {
                return;
            }

            int sizeOfReceivedData = -1;
            try
            {
                sizeOfReceivedData = Stream.EndRead(ar);
            }
            catch (Exception ex)
            {
                OnError(ex);
                Close();
                return;
            }
            if (sizeOfReceivedData > 0)
            {
                foreach (var c in dataBuffer)
                {
                    Frame(c);
                }
                dataBuffer = new byte[bufferSize];
                // continue listening for more data
                Listen();
            }
            else
            {
                OnError(new Exception("Connection closed"));
                Close();
            }
        }

        private void SendHeartBeat(string message)
        {
            var reply = String.Format("~h~{0}", message.Substring(message.LastIndexOf("~") + 1));
            Send(reply);
        }

        /// <summary>
        /// Closes the socket
        /// </summary>
        public void Dispose()
        {
            Close();
            Stream = null;
        }

        #endregion        
    }
}

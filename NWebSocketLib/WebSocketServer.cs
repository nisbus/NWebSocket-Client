using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;

namespace NWebSocketLib
{
    public enum ServerLogLevel { Nothing, Subtle, Verbose };
    public delegate void ClientConnectedEventHandler(WebSocketConnection sender, EventArgs e);

    /// <summary>
    /// This class was downloaded from http://nugget.codeplex.com/
    /// 
    /// Copyright (c) 2010 
    ///Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
    ///The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
    /// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
    /// </summary>
    public class WebSocketServer
    {
        #region private members

        private string webSocketOrigin;     // location for the protocol handshake
        private string webSocketLocation;   // location for the protocol handshake
        private Subject<ClientInfo> clientSocketEvents = new Subject<ClientInfo>();
        private Subject<Log> loggerEvents = new Subject<Log>();
        private Subject<Tuple<WebSocketConnection, string>> onMessage = new Subject<Tuple<WebSocketConnection,string>>();

        #endregion

        #region Properties

        public IQbservable<ClientInfo> ClientSocketEvents
        {
            get
            {
                return clientSocketEvents.AsQbservable();
            }
        }

        public IQbservable<Log> Logger
        {
            get
            {
                return loggerEvents.AsQbservable();
            }
        }

        public IQbservable<Tuple<WebSocketConnection, string>> OnMessage
        {
            get { return onMessage.AsQbservable(); }
        }

        /// <summary>
        /// How much information do you want, the server to post to the stream
        /// </summary>
        public ServerLogLevel LogLevel = ServerLogLevel.Subtle;

        /// <summary>
        /// Gets the connections of the server
        /// </summary>
        public List<WebSocketConnection> Connections { get; private set; }

        /// <summary>
        /// Gets the listener socket. This socket is used to listen for new client connections
        /// </summary>
        public Socket ListenerSocker { get; private set; }

        /// <summary>
        /// Get the port of the server
        /// </summary>
        public int Port { get; private set; }

        #endregion

        #region Ctor

        public WebSocketServer(int port, string origin, string location)
        {
            Port = port;
            Connections = new List<WebSocketConnection>();
            webSocketOrigin = origin;
            webSocketLocation = location;
        }

        #endregion

        #region API

        /// <summary>
        /// Starts the server - making it listen for connections
        /// </summary>
        public void Start()
        {
            // create the main server socket, bind it to the local ip address and start listening for clients
            ListenerSocker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            IPEndPoint ipLocal = new IPEndPoint(IPAddress.Loopback, Port);
            ListenerSocker.Bind(ipLocal);
            ListenerSocker.Listen(100);
            loggerEvents.OnNext(new Log(string.Format("{0} > server stated on {1}", DateTime.Now, ListenerSocker.LocalEndPoint), ServerLogLevel.Subtle));
            ListenForClients();
        }

        public void Stop()
        {
            ListenerSocker.Close();

            foreach (var conn in Connections)
            {
                conn.Close();
            }
        }

        #endregion

        #region Methods

        // look for connecting clients
        private void ListenForClients()
        {
            ListenerSocker.BeginAccept(new AsyncCallback(OnClientConnect), null);
        }

        private void OnClientConnect(IAsyncResult asyn)
        {
            var clientSocket = ListenerSocker.EndAccept(asyn);
            byte[] buffer = new byte[1024];
            clientSocket.BeginReceive(buffer, 0, 1024, 0, new AsyncCallback(ShakeHands), new Tuple<byte[], Socket>(buffer,clientSocket));
        }

        void ClientDisconnected(WebSocketConnection sender, EventArgs e)
        {
            Connections.Remove(sender);
            loggerEvents.OnNext(new Log(string.Format("{0} > {1} disconnected", DateTime.Now, sender.Socket.LocalEndPoint), ServerLogLevel.Subtle));
        }

        void DataReceivedFromClient(WebSocketConnection sender, string data)
        {
            clientSocketEvents.OnNext(new ClientInfo(SocketInfoCode.Received, data));
            onMessage.OnNext(new Tuple<WebSocketConnection, string>(sender, data));
            loggerEvents.OnNext(new Log(string.Format("{0} > data ({1} bytes) from {2} : {3}", DateTime.Now, Encoding.UTF8.GetByteCount(data), sender.Socket.LocalEndPoint,data), ServerLogLevel.Verbose));
        }

        /// <summary>
        /// send a string to all the clients (you spammer!)
        /// </summary>
        /// <param name="data">the string to send</param>
        public void SendToAll(string data)
        {
            Connections.ForEach(a => a.Send(data));
        }

        /// <summary>
        /// send a string to all the clients except one
        /// </summary>
        /// <param name="data">the string to send</param>
        /// <param name="indifferent">the client that doesn't care</param>
        public void SendToAllExceptOne(string data, WebSocketConnection indifferent)
        {
            foreach (var client in Connections)
            {
                if (client != indifferent)
                    client.Send(data);
            }
        }

        /// <summary>
        /// Takes care of the initial handshaking between the the client and the server
        /// </summary>
        private void ShakeHands(IAsyncResult ar)
        {
            var bufferAndSocket = (Tuple<byte[], Socket>)ar.AsyncState;
            var receivedByteCount = bufferAndSocket.Item2.EndReceive(ar);
            var clientHandshake = HandshakeHelper.ParseClientHandshake(new ArraySegment<byte>(bufferAndSocket.Item1, 0, receivedByteCount));
            var response = HandshakeHelper.GenerateResponseHandshake(clientHandshake);
            bufferAndSocket.Item2.BeginSend(response, 0, response.Length, 0, EndSendServerHandshake, bufferAndSocket.Item2);
        }

        private void EndSendServerHandshake(IAsyncResult ar)
        {
            Socket socket = (Socket)ar.AsyncState;
            socket.EndSend(ar);

            loggerEvents.OnNext(new Log(DateTime.Now + "> new connection from " + socket.LocalEndPoint, ServerLogLevel.Subtle));

            // keep track of the new guy
            var clientConnection = new WebSocketConnection(socket);
            Connections.Add(clientConnection);
            clientConnection.OnSocketInfo.Where(si => si.Code == SocketInfoCode.Closed).Subscribe(_ =>
            {
                ClientDisconnected(clientConnection, EventArgs.Empty);
            });

            // invoke the connection event
            clientSocketEvents.OnNext(new ClientInfo(SocketInfoCode.Connected, ""));

            if (LogLevel != ServerLogLevel.Nothing)
            clientConnection.OnMessage.Subscribe(x =>
            {
                DataReceivedFromClient(clientConnection, x);
            });
            // listen for more clients
            ListenForClients();
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Collections.Specialized;
using System.Net;

namespace NWebSocketLib
{
    /// <summary>
    /// WebSocket client.
    /// </summary>
    /// <remarks>
    /// This class design to mimic JavaScript's WebSocket class.
    /// It handle the handshake and framing of the data and provides OnMessage IQbservable event to handle messages.
    /// Please note that this event is raised on a arbitrary pooled background thread.
    /// See the SubscribeOnDispatcher to raise the event on the UI thread.
    /// </remarks>
    public class WebSocketClient
    {
        #region Fields

        private readonly static byte[] CloseFrame = new byte[] { 0xFF, 0x00 };

        private Uri uri;
        private Socket socket;
        private bool handshakeComplete;
        private NetworkStream networkStream;
        private StreamReader inputStream;
        private StreamWriter outputStream;
        private WebSocketConnection connection;
        private Dictionary<string, string> headers;
        private Func<string, dynamic> messageConversionFunc;

        private Subject<dynamic> onMessage = new Subject<dynamic>();
        private Subject<SocketInfo> onSocketInfo = new Subject<SocketInfo>();
        private IDisposable socketInfoSubscription;
        private IDisposable messageSubscription;

        #endregion

        #region Properties

        /// <summary>
        /// Extra headers to be sent
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get { return headers; }
        }

        public IQbservable<dynamic> OnMessage
        {
            get
            {
                return onMessage.AsQbservable();
            }
        }

        public IQbservable<SocketInfo> OnSocketInfo
        {
            get
            {
                return onSocketInfo.AsQbservable();
            }
        }
        
        public bool IsConnected
        {
            get
            {
                return this.socket != null && this.socket.Connected;
            }
        }

        #endregion

        #region Ctor

        /// <summary>
        /// Creates a new websocket client
        /// </summary>
        /// <param name="uri">The uri to connect to</param>
        public WebSocketClient(Uri uri)
        {
            this.messageConversionFunc = (msg) => { return msg; };
            Initialize(uri);
        }

        /// <summary>
        /// Creates a new WebSocket targeting the specified URL.
        /// </summary>
        /// <param name="uri">the uri to connect to</param>
        /// <param name="messageConversionFunc">A function to convert messages to i.e. .NET objects</param>
        public WebSocketClient(Uri uri, Func<string, dynamic> messageConversionFunc)
        {
            this.messageConversionFunc = messageConversionFunc;
            Initialize(uri);
        }

        #endregion

        #region API

        /// <summary>
        /// Establishes the connection
        /// </summary>
        public void Connect()
        {            
            string host = uri.Host;
            StringBuilder path = new StringBuilder(uri.AbsolutePath);
            if (path.Length == 0)
            {
                path.Append('/');
            }

            string query = uri.Query;
            if (!string.IsNullOrEmpty(query))
            {
                path.Append("?");
                path.Append(query);
            }

            string origin = "http://" + host;

            socket = CreateSocket();
            
            IPEndPoint localEndPoint = (IPEndPoint)socket.LocalEndPoint;
            int port = localEndPoint.Port;
            if (port != 80)
            {
                host = host + ":" + port;
            }
            
            ClientHandshake shake = new ClientHandshake();
            shake.Host = host;
            shake.Origin = origin;
            shake.AdditionalFields = headers;
            shake.Key1 = Guid.NewGuid().ToString();
            shake.Key2 = Guid.NewGuid().ToString();
            shake.Key1 = shake.Key1.Replace('-', ' ').Substring(0,10);
            shake.Key2 = shake.Key2.Replace('-', ' ').Substring(0, 10);
            var baseChallenge = Guid.NewGuid().ToString().Substring(0,8);
            var challenge = baseChallenge.Substring(0, 2) + " " + baseChallenge.Substring(3, 2) + " " + baseChallenge.Substring(5, 2);
            shake.ChallengeBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(challenge));
            shake.ResourcePath = path.ToString();
            networkStream = new NetworkStream(socket);
            outputStream = new StreamWriter(networkStream, Encoding.UTF8);
            var response = shake.ToString();
            byte[] encodedHandshake = Encoding.UTF8.GetBytes(response);
            networkStream.Write(encodedHandshake, 0, encodedHandshake.Length);
            networkStream.Flush();
            var expectedAnswer = Encoding.UTF8.GetString(HandshakeHelper.CalculateAnswerBytes(shake.Key1, shake.Key2, shake.ChallengeBytes));

            inputStream = new StreamReader(networkStream);
            //var input = inputStream.ReadToEnd();
            string header = inputStream.ReadLine();
            if (!header.Equals("HTTP/1.1 101 WebSocket Protocol Handshake"))
            {
                onSocketInfo.OnError(new InvalidOperationException("Invalid handshake response"));
                throw new InvalidOperationException("Invalid handshake response");
            }
            
            header = inputStream.ReadLine();
            if (!header.Equals("Upgrade: WebSocket"))
            {
                onSocketInfo.OnError(new InvalidOperationException("Invalid handshake response"));
                throw new InvalidOperationException("Invalid handshake response");
            }

            header = inputStream.ReadLine();
            if (!header.Equals("Connection: Upgrade"))
            {
                onSocketInfo.OnError(new InvalidOperationException("Invalid handshake response"));
                throw new InvalidOperationException("Invalid handshake response");
            }

            // Ignore any further response
            do
            {
                header = inputStream.ReadLine();
                Console.WriteLine(header);
            } while (!header.Equals(""));

           // var answer = inputStream.ReadLine();
           // Console.WriteLine(answer);
            handshakeComplete = true;

            connection = new WebSocketConnection(socket);
            SubscribeToConnectionEvents();
        }

        private void SubscribeToConnectionEvents()
        {
            socketInfoSubscription = connection.OnSocketInfo.Subscribe(x =>
            {
                onSocketInfo.OnNext(x);
            }, (exception) =>
            {
                onSocketInfo.OnError(exception);
            }, () =>
            {
                onSocketInfo.OnCompleted();
            });

            connection.OnMessage.Subscribe(x =>
            {
                onMessage.OnNext(messageConversionFunc.Invoke(x));
            }, (exception) =>
            {
                onMessage.OnError(exception);
            }, () =>
            {
                onMessage.OnCompleted();
            });
        }

        /// <summary>
        /// Closes the socket.
        /// </summary>
        public void Close()
        {
            if (handshakeComplete)
            {
                try
                {
                    networkStream.Write(CloseFrame, 0, CloseFrame.Length);
                    networkStream.Flush();
                }
                catch
                {
                    // Ignore any excption during close handshake.
                }
            }

            connection.Close();
            onSocketInfo.OnNext(new SocketInfo(SocketInfoCode.Closed, "Bye bye"));
            onSocketInfo.OnCompleted();
            onMessage.OnCompleted();
        }

        /// <summary>
        /// Sends the specified string as a data frame.
        /// </summary>
        /// <param name="payload"></param>
        public void Send(string payload)
        {
            DemandHandshake();

            networkStream.WriteByte(0x00);
            byte[] encodedPayload = Encoding.UTF8.GetBytes(payload);
            networkStream.Write(encodedPayload, 0, encodedPayload.Length);
            networkStream.WriteByte(0xFF);
            networkStream.Flush();
        }

        #endregion

        #region Methods

        private void Initialize(Uri uri)
        {
            this.uri = uri;
            string protocol = uri.Scheme;
            if (!protocol.Equals("ws") && !protocol.Equals("wss"))
            {
                throw new ArgumentException("Unsupported protocol: " + protocol);
            }
            headers = new Dictionary<string, string>();
        }

        private void DemandHandshake()
        {
            if (!handshakeComplete)
            {
                throw new InvalidOperationException("Handshake not complete yet");
            }
        }

        private Socket CreateSocket()
        {
            string scheme = uri.Scheme;
            string host = uri.Host;

            int port = uri.Port;
            if (port == -1)
            {
                if (scheme.Equals("wss"))
                {
                    port = 443;
                }
                else if (scheme.Equals("ws"))
                {
                    port = 80;
                }
                else
                {
                    throw new ArgumentException("Unsupported scheme");
                }
            }

            if (scheme.Equals("wss"))
            {
                throw new NotSupportedException("Not support secure WebSocket yet");
                //SocketFactory factory = SSLSocketFactory.getDefault();
                //return factory.createSocket(host, port);
            }
            else
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(host, port);
                return socket;
            }
        }

        #endregion
    }
}

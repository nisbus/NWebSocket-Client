using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.IO;
using System.Collections.Specialized;
using System.Net;
using System.Text.RegularExpressions;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace NWebSocketLib
{
    public interface IMessageSource
    {
        event Action<string> OnMessage;
    }

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

        private string certPath = "";

        private bool handshakeComplete;
        private Stream networkStream;

        private StreamReader inputStream;
        private WebSocketConnection connection;
        private Dictionary<string, string> headers;
        private Func<string, dynamic> messageConversionFunc;

        private bool isSocketIo;

        #endregion

        #region Properties

        /// <summary>
        /// Extra headers to be sent
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get { return headers; }
            set { headers = value; }
        }

        public bool IsConnected
        {
            get { return connection != null && connection.Stream != null && connection.IsConnected;}
        }

        public event Action<string> OnMessage;

        public event Action<Exception> OnError;

        #endregion

        #region Ctor

        /// <summary>
        /// Creates a new WebSocket targeting the specified URL.
        /// </summary>
        /// <param name="uri">the uri to connect to</param>
        /// <param name="messageConversionFunc">A function to convert messages to i.e. .NET objects</param>
        public WebSocketClient(Uri uri, Func<string, dynamic> messageConversionFunc, bool isSocketIo)
        {
            this.isSocketIo = isSocketIo;
            this.messageConversionFunc = messageConversionFunc;
            Initialize(uri);
        }

        /// <summary>
        /// Creates a new websocket client
        /// </summary>
        /// <param name="uri">The uri to connect to</param>
        public WebSocketClient(Uri uri) : this(uri, (msg) => { return msg; }, false) { }

        /// <summary>
        /// Creates a new websocket client
        /// </summary>
        /// <param name="uri">The uri to connect to</param>
        public WebSocketClient(Uri uri, bool isSocketIo, string certPath) : this(uri, (msg) => { return msg; }, isSocketIo) 
        {
            this.certPath = certPath;
        }

        /// <summary>
        /// Creates a new websocket client
        /// </summary>
        /// <param name="uri">The uri to connect to</param>
        public WebSocketClient(Uri uri, bool isSocketIo)
            : this(uri, (msg) => { return msg; }, isSocketIo)
        {
        }


        #endregion

        #region API

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

            networkStream = CreateSocket();
            if (networkStream != null)
            {
                if (uri.Port != 80)
                {
                    host = host + ":" + uri.Port;
                }

                ClientHandshake shake = new ClientHandshake();
                shake.Host = host;
                shake.Origin = origin;
                shake.AdditionalFields = headers;
                shake.Key1 = Guid.NewGuid().ToString();
                shake.Key2 = Guid.NewGuid().ToString();
                shake.Key1 = shake.Key1.Replace('-', ' ').Substring(0, 10);
                shake.Key2 = shake.Key2.Replace('-', ' ').Substring(0, 10);
                var baseChallenge = Guid.NewGuid().ToString().Substring(0, 8);
                var challenge = baseChallenge.Substring(0, 2) + " " + baseChallenge.Substring(3, 2) + " " + baseChallenge.Substring(5, 2);
                shake.ChallengeBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(challenge));
                shake.ResourcePath = path.ToString();
                //outputStream = new StreamWriter(networkStream, Encoding.UTF8);
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
                    if (OnError != null)
                        OnError(new InvalidOperationException("Invalid handshake response"));
                    throw new InvalidOperationException("Invalid handshake response");
                }

                header = inputStream.ReadLine();
                if (!header.Equals("Upgrade: WebSocket"))
                {
                    if (OnError != null)
                        OnError(new InvalidOperationException("Invalid handshake response"));
                    throw new InvalidOperationException("Invalid handshake response");
                }

                header = inputStream.ReadLine();
                if (!header.Equals("Connection: Upgrade"))
                {
                    if (OnError != null)
                        OnError(new InvalidOperationException("Invalid handshake response"));
                    throw new InvalidOperationException("Invalid handshake response");
                }

                // Ignore any further response
                do
                {
                    header = inputStream.ReadLine();
                } while (!header.Equals(""));

                handshakeComplete = true;

                connection = new WebSocketConnection(networkStream, isSocketIo);
                SubscribeToConnectionEvents();
            }
            else
            {
                throw new InvalidOperationException("Could not create socket");
            }
        }

        private void SubscribeToConnectionEvents()
        {
            connection.OnMessage += new Action<string>(connection_OnMessage);
            connection.OnError += new Action<Exception>(connection_OnError);
            connection.Listen();
        }

        void connection_OnMessage(string obj)
        {
            if (OnMessage != null)
                OnMessage(obj);
        }

        void connection_OnError(Exception ex)
        {
            if (this.OnError != null)
                OnError(ex);
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
        }

        /// <summary>
        /// Sends the specified string as a data frame.
        /// </summary>
        /// <param name="payload"></param>
        public void Send(string payload)
        {
            DemandHandshake();
            connection.Send(payload);
        }

        #endregion

        #region Methods

        private void DemandHandshake()
        {
            if (!handshakeComplete)
            {
                throw new InvalidOperationException("Handshake not complete yet");
            }
        }

        private Stream CreateSocket()
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
            try
            {
                if (scheme.Equals("wss"))
                {
                    TcpClient socket = new TcpClient(host, port);
                    SslStream s = new SslStream(socket.GetStream(), true, ValidateServerCertificate, SelectLocalCertificate);

                    s.AuthenticateAsClient(host);
                    return s;
                }
                else
                {
                    TcpClient socket = new TcpClient(host, port);
                    return socket.GetStream();
                }
            }
            catch (Exception ex)
            {
                if (this.OnError != null)
                    this.OnError(ex);
                return null;
            }
        }

        public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
            return false;
        }

        public X509Certificate SelectLocalCertificate(
            object sender,
            string targetHost,
            X509CertificateCollection localCertificates,
            X509Certificate remoteCertificate,
            string[] acceptableIssuers)
        {
            try
            {
                if(string.IsNullOrEmpty(certPath) == false)
                    return X509Certificate.CreateFromCertFile(certPath);

                return null;
            }
            catch
            {
                throw;
            }
        }

        #endregion        
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NWebSocketLib;

namespace ClientConsole
{
    class Program
    {
        static WebSocketClient client;
        static void Main(string[] args)
        {
            client = new WebSocketClient(new Uri("ws://127.0.0.1:8181"));
            client.OnSocketInfo.Subscribe(x =>
            {
                Console.WriteLine(x.Message);
            }, (ex) =>
            {
                Console.WriteLine("SocketError : " + ex.Message);
            });
            client.OnMessage.Subscribe(x =>
            {
                Console.WriteLine("Received " + x);
            }, (ex) =>
            {
                Console.WriteLine("Error receiving :" + ex.Message);
            });
            client.Connect();
            Console.WriteLine("Client started...Type a message to send");
            Send();
        }

        private static void Send()
        {
            var msg = Console.ReadLine();
            client.Send(msg);
            Console.WriteLine("Sent...Type another message");
            Send();

        }
    }
}

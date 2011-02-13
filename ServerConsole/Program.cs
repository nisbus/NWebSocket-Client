using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NWebSocketLib;

namespace ServerConsole
{
    class Program
    {
        static WebSocketServer server;
        static void Main(string[] args)
        {
            server = new WebSocketServer(8181, null, "ws://localhost:8181");
            server.ClientSocketEvents.Subscribe(info =>
            {
                Console.WriteLine("Client socket: " + info.Message);
            },
            (ex) =>
            {
                Console.WriteLine("Client socket error :", ex);
            });
            server.Logger.Subscribe(x =>
            {
                Console.WriteLine(x.LogMessage);
            });

            server.OnMessage.Subscribe(msg =>
            {
                Console.WriteLine(string.Format("{0} - sent message : {1}", msg.Item1, msg.Item2));
                msg.Item1.Send(string.Format("Echo - {0}", msg.Item2));
            },
            (ex) =>
            {
                Console.WriteLine("Error in message loop: " + ex.Message);
            });
            
            server.Start();
            Console.WriteLine("Started server....Press any key to send to clients");
            SendToAll();
        }

        private static void SendToAll()
        {
            var msg = Console.ReadLine();
            server.SendToAll(msg);
            SendToAll();
        }
    }
}

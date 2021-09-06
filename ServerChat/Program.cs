using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NetCoreServer;

namespace ServerChat
{
    class ChatSession : TcpSession
    {
        public ChatSession(TcpServer server) : base(server) { }
        public bool test;

        protected override void OnConnected()
        {
            //Console.WriteLine($"Chat TCP session with Id {Id} connected!");

            // Send invite message
            //string message = "Hello from TCP chat! Please send a message or '!' to disconnect the client!";
            //SendAsync(message);
        }

        protected override void OnDisconnected()
        {
            Console.WriteLine($"Chat TCP session with Id {Id} disconnected!");
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            //string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
            //Console.WriteLine("Incoming: " + message);
            Console.Write(".");

            // Multicast message to all connected sessions
            //Server.Multicast(message);

            // If the buffer starts with '!' the disconnect the current session
            //if (message == "!")
            //Disconnect();
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP session caught an error with code {error}");
        }
    }

    class ChatServer : TcpServer
    {
        internal ConcurrentDictionary<Guid, TcpSession> sessions { get => Sessions; }
        public ChatServer(IPAddress address, int port) : base(address, port) { }

        protected override TcpSession CreateSession() { return new ChatSession(this); }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Chat TCP server caught an error with code {error}");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // TCP server port
            int port = 5000;

            Console.WriteLine($"TCP server port: {port}");

            Console.WriteLine();

            // Create a new TCP chat server
            var server = new ChatServer(IPAddress.Any, port);

            // Start the server
            Console.Write("Server starting...");
            server.Start();
            Console.WriteLine("Done!");

            Console.WriteLine("Press Enter to stop the server or '!' to restart the server...");

            // Perform text input
            byte[] sendBuff = new byte[10000];
            Random rd = new Random();
            rd.NextBytes(sendBuff);
            for (; ; )
            {
                server.Multicast(sendBuff);
                Thread.Sleep(1000);
                Console.Write(server.ConnectedSessions);
            }
        }
    }
}

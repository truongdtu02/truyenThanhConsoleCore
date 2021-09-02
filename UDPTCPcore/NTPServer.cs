using System;
using Microsoft.Extensions.Logging;
using System.Net;
using NetCoreServer;
using System.Net.Sockets;

namespace UDPTCPcore
{
    class NTPServer : UdpServer
    {
        private readonly ILogger<NTPServer> _log;
        int _port;
        public NTPServer(IPAddress address, int port, ILogger<NTPServer> log) : base(address, port)
        {
            _log = log;
            _port = port;
        }

        protected override void OnStarted()
        {
            _log.LogInformation($"UDP port:{_port} start!!!");
            // Start receive datagrams
            ReceiveAsync();
        }
        
        protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {
            //Console.WriteLine("Incoming: " + Encoding.UTF8.GetString(buffer, (int)offset, (int)size));
            if(size == 4)
            {
                byte[] ntpBuffer = new byte[4 + 8]; //4B client time and 8B server time
                System.Buffer.BlockCopy(buffer, (int)offset, ntpBuffer, 0, 4);
                System.Buffer.BlockCopy(BitConverter.GetBytes(DateTimeOffset.Now.ToUnixTimeMilliseconds()), 0, ntpBuffer, 4, sizeof(long));
                SendAsync(endpoint, ntpBuffer, 0, 12);
            }
        }

        protected override void OnSent(EndPoint endpoint, long sent)
        {
            // Continue receive datagrams
            ReceiveAsync();
        }

        protected override void OnError(SocketError error)
        {
            _log.LogInformation($"UDP error {error}");
        }
    }
}

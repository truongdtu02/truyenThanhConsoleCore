﻿using System;
using Microsoft.Extensions.Logging;
using System.Net;
using NetCoreServer;
using System.Net.Sockets;
using System.Text;
using Security;

namespace UDPTCPcore
{
    class NTPServer : UdpServer
    {
        private readonly ILogger<NTPServer> _log;
        int _port;
        byte[] ntpAESkey;
        public NTPServer(IPAddress address, int port, ILogger<NTPServer> log) : base(address, port)
        {
            _log = log;
            _port = port;
        }

        protected override void OnStarted()
        {
            string ntpAESkeyString = "dayLaAESKeyNtp!!";
            ntpAESkey = Encoding.UTF8.GetBytes(ntpAESkeyString);

            _log.LogInformation($"UDP port:{_port} start!!!");
            // Start receive datagrams
            ReceiveAsync();
        }
        
        protected override void OnReceived(EndPoint endpoint, byte[] buffer, long offset, long size)
        {
            //Console.WriteLine("Incoming: " + Encoding.UTF8.GetString(buffer, (int)offset, (int)size));
            if(size == 32)
            {
                byte[] checkSum1 = AES.AES_Decrypt(buffer, (int)offset, 16, ntpAESkey, false);

                byte[] checkSum2 = MD5.MD5Hash(buffer, (int)offset + 16, 16);

                for(int i = 0; i < 16; i++)
                {
                    if (checkSum1[i] != checkSum2[i]) return;
                }

                byte[] decrypted = AES.AES_Decrypt(buffer, (int)offset + 16, 16, ntpAESkey, false);

                long curTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                System.Buffer.BlockCopy(BitConverter.GetBytes(curTime), 0, decrypted, 0, sizeof(long));

                byte[] encrypted = AES.AES_Encrypt(decrypted, 0, decrypted.Length, ntpAESkey);

                byte[] checkSum = MD5.MD5Hash(encrypted, 0, encrypted.Length);

                checkSum = AES.AES_Encrypt(checkSum, 0, checkSum.Length, ntpAESkey);

                byte[] sendBuff = new byte[checkSum.Length + encrypted.Length];

                System.Buffer.BlockCopy(checkSum, 0, sendBuff, 0, checkSum.Length);
                System.Buffer.BlockCopy(encrypted, 0, sendBuff, checkSum.Length, encrypted.Length);

                SendAsync(endpoint, sendBuff);

                //Console.WriteLine(DateTimeOffset.Now);

                //byte[] ntpBuffer = new byte[4 + 8]; //4B client time and 8B server time
                //System.Buffer.BlockCopy(buffer, (int)offset, ntpBuffer, 0, 4);
                //System.Buffer.BlockCopy(BitConverter.GetBytes(DateTimeOffset.Now.ToUnixTimeMilliseconds()), 0, ntpBuffer, 4, sizeof(long));
                //SendAsync(endpoint, ntpBuffer, 0, 12);
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

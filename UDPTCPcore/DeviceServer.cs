using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Net;
using System.Collections.Generic;
using MP3_ADU;
using System.Diagnostics;
using System.Threading;
using System.Timers;

namespace UDPTCPcore
{
    class DeviceServer : TcpServer
    {
        private readonly ILogger<DeviceServer> _log;

        //DeviceSession deviceSession;
        //internal ChatSession ChatSession { get => chatSession; }
        //internal List<DeviceSession> listDeviceSession { get; private set; }
        internal ConcurrentDictionary<Guid, TcpSession> listSesions { get => Sessions; }
        public DeviceServer(IPAddress address, int port, ILogger<DeviceServer> log) : base(address, port)
        {
            _log = log;
            _log.LogInformation($"TCP server port: {port}");
            //listDeviceSession = new List<DeviceSession>();
        }

        private void TimeoutTimerEvent(Object source, ElapsedEventArgs e)
        {
            //int maxIndx = listDeviceSession.Count;
            //for(int i = maxIndx - 1; i >= 0; i--)
            //{
            //    if(listDeviceSession[i].bNeedRemove)
            //    {
            //        listDeviceSession.RemoveAt(i);
            //    }
            //}
        }

        internal void Run()
        {
            // Create a timer to handle connect time-out
            var timeoutTimer = new System.Timers.Timer(5000);
            // Hook up the Elapsed event for the timer. 
            timeoutTimer.Elapsed += TimeoutTimerEvent;
            timeoutTimer.AutoReset = true;
            timeoutTimer.Enabled = true;

            // Start the server
            _log.LogInformation("Server starting...");
            Start();
            _log.LogInformation("Server Done!");

            List<string> soundList;
            if (OperatingSystem.IsWindows())
            {
                soundList = new List<string>()
                {
                    @"E:\truyenthanhproject\mp3\bai1.mp3",
                    @"E:\truyenthanhproject\mp3\bai2.mp3",
                    @"E:\truyenthanhproject\mp3\bai3.mp3"
                };
            }
            else
            {
                soundList = new List<string>()
                {
                    "bai1.mp3",
                    "bai2.mp3",
                    "bai3.mp3"
                };
            }

            const int NUM_OF_FRAME_SEND_PER_PACKET = 43;
            const int MAX_MAIN_DATA_BEGIN_BYTES = (int)1 << 9 ;
            const int FRAME_SIZE = 144;
            const int FRAME_TIME_MS = 24;

            long curTime, startTime1, endTime1, startTime2, endTime2;
            int timeOutSend = 0;
            while (true)
            {
                
                //send 
                foreach(var song in soundList)
                {
                    using(FileStream mp3Song = new FileStream(song, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        MP3_Frame_CBR mp3Read = new MP3_Frame_CBR(mp3Song);
                        if (!mp3Read.CheckValidMP3(2, 48, 24000)) continue;

                        Stopwatch sendWatch = new Stopwatch();
                        sendWatch.Start();
                        int sendTime = 0;
                        UInt32 frameID = 0, oldFrameID = 0;
                        //read frame and send
                        while(true)
                        {
                            //debug
                            startTime1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            startTime2 = sendWatch.ElapsedMilliseconds;

                            //get frame
                            int totalLen = 0;
                            byte[] tmp;
                            List<byte[]> mp3FrameList = new List<byte[]>();
                            for (int i = 0; i < NUM_OF_FRAME_SEND_PER_PACKET; i++)
                            {
                                if(i == 0)
                                {
                                    tmp = mp3Read.ReadNextADU();
                                }
                                else
                                {
                                    tmp = mp3Read.ReadNextFrame(false);
                                }
                                if (tmp == null) break;
                                totalLen += tmp.Length;
                                mp3FrameList.Add(tmp);
                                frameID++;
                            }
                            if(totalLen > 0)
                            {
                                curTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                byte[] sendBuff= MP3PacketHeader.Packet(mp3FrameList, 100, curTime, oldFrameID, (UInt16)(mp3Read.Frame_size - 4), (byte)mp3Read.TimePerFrame_ms, totalLen);

                                //send packet
                                //foreach(var dv in listDeviceSession)
                                //{
                                //    dv.SendMP3PackAssync(sendBuff, 1, "bom", curTime, MP3PacketHeader.HEADER_NOENCRYPT_SIZE);
                                //    //dv.SendPackAssync(sendBuff, sendBuff.Length, DeviceSession.SendPackeTypeEnum.PacketMP3);
                                //}

                                // Multicast data to all sessions
                                foreach (var session in Sessions.Values)
                                {
                                    var dv = (DeviceSession)session;
                                    dv.SendMP3PackAssync(sendBuff, 1, "bom", curTime, MP3PacketHeader.HEADER_NOENCRYPT_SIZE, false);
                                }
                            }
                            else
                            {
                                break;
                            }
                            oldFrameID = frameID;
                            sendTime ++;
                            long offsetTime = sendTime * NUM_OF_FRAME_SEND_PER_PACKET * FRAME_TIME_MS - sendWatch.ElapsedMilliseconds;

                            //debug
                            endTime1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            endTime2 = sendWatch.ElapsedMilliseconds;

                            if (offsetTime > 0)
                            {
                                Thread.Sleep((int)offsetTime);
                            } else if(offsetTime < 0)
                            {
                                //_log.LogError($"Time1: {startTime1} - {endTime1} , Time2: {startTime2} - {endTime2}");
                                timeOutSend++;
                            }

                            _log.LogError($"Time1: {startTime1} - {endTime1} , Time2: {startTime2} - {endTime2}");

                            if (sendTime % 30 == 0)
                            {
                                _log.LogError($"Num of dev: {ConnectedSessions}, send time-out: {timeOutSend}");
                            }

                            //dipose
                            mp3FrameList.Clear();
                        }
                    }
                }
            }
        }

        protected override TcpSession CreateSession()
        {
            return Program.host.Services.GetRequiredService<DeviceSession>();
        }

        protected override void OnError(SocketError error)
        {
            _log.LogError($"DeviceServer error {error}");
        }
        protected override void OnConnected(TcpSession session)
        {
            base.OnConnected(session);
        }
    }
}

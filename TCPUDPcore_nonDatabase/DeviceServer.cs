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
using Newtonsoft.Json;

namespace UDPTCPcore
{
    class deviceID
    {
        [JsonProperty("listID")]
        private List<string> listID;
    }
    class DeviceServer : TcpServer
    {
        private readonly ILogger<DeviceServer> _log;
        private List<string> _listID;
        private Object lockObj = new Object();
        internal void updateListID(List<string> listID)
        {
            var timeout = TimeSpan.FromMilliseconds(10);
            bool lockTaken = false;

            try
            {
                Monitor.TryEnter(lockObj, timeout, ref lockTaken);
                if (lockTaken)
                {
                    // The critical section.
                    if(_listID != null)
                        _listID.Clear();
                    _listID = listID;
                }
                else
                {
                    // The lock was not acquired.
                    _log.LogError("Can't access listID");
                }
            }
            finally
            {
                // Ensure that the lock is released.
                if (lockTaken)
                {
                    Monitor.Exit(lockObj);
                }
            }
        }

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

        void SendTimerEvent(Object source, ElapsedEventArgs e, CountdownEvent _countdown)
        {
            if(_countdown.CurrentCount != 0)
            {
                _countdown.Signal();
            }
        }

        void InitiliazeSendTimer(System.Timers.Timer sendTimer, CountdownEvent _countdown, double interval)
        {
            // Create a timer to handle connect time-out
            if(sendTimer == null)
                sendTimer = new System.Timers.Timer(interval);

            // Hook up the Elapsed event for the timer. 
            sendTimer.Elapsed += (sender, e) => SendTimerEvent(sender, e, _countdown);
            sendTimer.AutoReset = true;
            sendTimer.Enabled = true;
        }

        UInt16 caculateChecksum(byte[] data, int offset, int length)
        {
            UInt32 checkSum = 0;
            int index = offset;
            while (length > 1)
            {
                checkSum += ((UInt32)data[index] << 8) | ((UInt32)data[index + 1]); //little edian
                length -= 2;
                index += 2;
            }
            if (length == 1) // still have 1 byte
            {
                checkSum += ((UInt32)data[index] << 8);
            }
            while ((checkSum >> 16) > 0) //checkSum > 0xFFFF
            {
                checkSum = (checkSum & 0xFFFF) + (checkSum >> 16);
            }
            //inverse
            checkSum = ~checkSum;
            return (UInt16)checkSum;
        }

        internal void Run()
        {
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

            const int NUM_OF_FRAME_SEND_PER_PACKET = 5;

            while (true)
            {
                //send 
                foreach(var song in soundList)
                {
                    //long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    using (FileStream mp3Song = new FileStream(song, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        MP3_Frame_CBR mp3Read = new MP3_Frame_CBR(mp3Song);
                        if (!mp3Read.CheckValidMP3(2, 48, 24000)) continue;

                        //Stopwatch sendWatch = new Stopwatch();
                        //sendWatch.Start();
                        CountdownEvent _countdown = new CountdownEvent(1);
                        double intervalSend = NUM_OF_FRAME_SEND_PER_PACKET * mp3Read.TimePerFrame_ms;
                        int _countdownTimeout = 2 * (int)intervalSend;
                        System.Timers.Timer sendTimer = new System.Timers.Timer(intervalSend);
                        InitiliazeSendTimer(sendTimer, _countdown, intervalSend);

                        int sendTime = 0;
                        int playTimeDelayms = 1000; //client delay play 1s with server play
                        UInt32 frameID = 0, oldFrameID = 0;
                        //read frame and send
                        //Stream fMp3 = File.OpenWrite(@"D:/adu.mp3");
                        while(true)
                        {
                            //get frame
                            int totalLen = 0;
                            byte[] tmp;
                            List<byte[]> mp3FrameList = new List<byte[]>();
                            for (int i = 0; i < NUM_OF_FRAME_SEND_PER_PACKET; i++)
                            {
                                if(i == 0)
                                {
                                    //debug
                                    //tmp = mp3Read.ReadNextADU();
                                    tmp = mp3Read.ReadNextFrame(true);
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
                                long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                //long frameTimestamp = sendTime * (long)intervalSend + startTime + playTimeDelayms;
                                long frameTimestamp = startTime + playTimeDelayms;
                                byte[] sendBuff= MP3PacketHeader.Packet(mp3FrameList, 100, frameTimestamp, oldFrameID, (UInt16)(mp3Read.Frame_size - 4), (byte)mp3Read.TimePerFrame_ms, totalLen);

                                //byte[] aduPac = new byte[5 * 144];
                                //System.Buffer.BlockCopy(mp3FrameList[0], 0, aduPac, 0, 144);
                                //for(int jd = 1; jd < 5; jd++)
                                //{
                                //    System.Buffer.BlockCopy(mp3FrameList[0], 0, aduPac, jd * 144, 4);
                                //    System.Buffer.BlockCopy(mp3FrameList[jd], 0, aduPac, jd * 144 + 4, 140);
                                //}

                                //UInt16 chsum = caculateChecksum(aduPac, 0, aduPac.Length);
                                //Console.WriteLine("{0} 0x{1:X}", oldFrameID, chsum);

                                //fMp3.Write(aduPac, 0, aduPac.Length);
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
                                    //dv.SendMP3PackAssync(sendBuff, 1, "bom", curTime, MP3PacketHeader.HEADER_NOENCRYPT_SIZE, false);

                                    //debug
                                    //int ofsset = 'z' - 'a' + 1;
                                    //for(int i = 1; i < sendBuff.Length; i++)
                                    //{
                                    //    if(i < 10)
                                    //    {
                                    //        sendBuff[i] = (byte)('A' + i % ofsset);
                                    //    }
                                    //    else if (i > (sendBuff.Length - 10))
                                    //    {
                                    //        sendBuff[i] = (byte)('A' + i % ofsset);
                                    //    }
                                    //    else
                                    //    {
                                    //        sendBuff[i] = (byte)('a' + i % ofsset);
                                    //    }
                                    //}
                                    //int len = sendBuff.Length; (BytesPending + sendPack.Length) < OptionSendBufferSize
                                    if (dv.IsHandshaked)
                                    {
                                        //if ((dv.BytesPending + sendBuff.Length) < dv.OptionSendBufferSize)
                                        //{
                                        //    dv.SendAsync(BitConverter.GetBytes(sendBuff.Length));
                                        //    System.Buffer.BlockCopy(BitConverter.GetBytes(order), 0, sendBuff, 0, 4);
                                        //    order++;
                                        //    dv.SendAsync(sendBuff);
                                        //    int tmpL = sendBuff.Length;
                                        //    _log.LogInformation($"MP3 {order}: {sendBuff[4]} {sendBuff[5]} {sendBuff[tmpL - 2]} {sendBuff[tmpL - 1]}");
                                        //}
                                        //else
                                        //{
                                        //    _log.LogInformation($"{dv.Token} miss");
                                        //}
                                        dv.SendMP3PackAssync(sendBuff, 1, "bom", frameTimestamp);
                                    }
                                }
                            }
                            else
                            {
                                break;
                    
                            }
                            oldFrameID = frameID;
                            //dipose
                            mp3FrameList.Clear();
                            bool res = _countdown.Wait(_countdownTimeout); //wait after interval
                            if (res) _countdown.Reset();

                            //debug
                            //long offsetInterval = DateTimeOffset.Now.ToUnixTimeMilliseconds() - (sendTime * (long)intervalSend + startTime);
                            //if(offsetInterval > 100 || _countdown.CurrentCount != 1 || !res)
                            //    _log.LogInformation($"Offset time: {offsetInterval} {_countdown.CurrentCount} {res}");
                            sendTime ++;
                        }
                        sendTimer.Close();
                        _countdown.Dispose();
                        //fMp3.Close();
                        Thread.Sleep(500); //gap between songs
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

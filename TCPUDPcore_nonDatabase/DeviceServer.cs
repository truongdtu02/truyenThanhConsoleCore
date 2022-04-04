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
using Serilog;

namespace UDPTCPcore
{
    class SessionPlay
    {
        internal List<Int32> listID;
        internal enum StateSession { play, pause, stop, none };
        internal StateSession state;
        internal string action, session;
        internal enum TypeSession { mp3, mic, link};
        internal TypeSession type;
        internal FileStream mp3Song;
        internal MP3_Frame_CBR mp3Read;
        List<byte[]> mp3FrameList = new List<byte[]>();
        internal UInt32 oldFrameID = 0, sessionMP3;
        internal int volume = 100, priority = 99;
        byte[] sendBuff;

        object lockSession = new object();

        internal void update(List<int> _listID)
        {

        }

        internal void mp3GenPlay(int numOfFrame, long startTime, long curTime)
        {
            byte[] tmp;
            int totalLen = 0;
            
            if (state == StateSession.pause)
                return;

            for (int i = 0; i < numOfFrame; i++)
            {
                if(i == 0)
                    tmp = mp3Read.ReadNextFrame(true);
                else
                    tmp = mp3Read.ReadNextFrame(false);

                if (tmp == null)
                {
                    state = StateSession.stop;
                    break;
                }
                totalLen += tmp.Length;
                mp3FrameList.Add(tmp);
            }

            if (totalLen > 0)
            {
                sendBuff = MP3PacketHeader.Packet(mp3FrameList, (int)DeviceSession.SendTLSPackeTypeEnum.PacketMP3, sessionMP3,
                    (byte)volume, startTime, oldFrameID, (UInt16)(mp3Read.Frame_size - 4), (byte)mp3Read.TimePerFrame_ms, totalLen);
            }

            mp3FrameList.Clear();

            if (sendBuff == null)
            {
                state = StateSession.stop;
                return;
            }    
            oldFrameID += (UInt32)numOfFrame;

            try
            {
                foreach (var id in listID)
                {
                    var dev = Program.deviceServer.listSesions.FirstOrDefault(d => ((DeviceSession)(d.Value)).ID == id).Value;
                    if (dev != null)
                    {
                        ((DeviceSession)dev).PrepareMP3PackAssync(sendBuff, priority, curTime);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception search device {0}", ex.Message);
            }
        }
    }

    class DeviceServer : TcpServer
    {
        private readonly ILogger<DeviceServer> _log;
        private List<Int32> _listID;
        private Object lockListID = new Object();
        private Object lockListSessionPlay = new Object();
        private List<SessionPlay> _listSessionPlay = new List<SessionPlay>();
        private List<SessionPlay> _listSessionStop = new List<SessionPlay>();

        string updateListSesioPlay_MP3(SessionPlay s)
        {
            string res, detail;

            bool sessionExist = false;
            int sessionIndex = -2;
            int doUpdateSession = 0; //1: new, 2:change
            bool updateFail = true;

            if (s == null)
            {
                detail = "session is invalid";
                goto parse_response;
            }

            if((s.action != "new" && s.action != "change") || (s.state == SessionPlay.StateSession.none)) 
            {
                detail = "request is invalid (wrong param)";
                goto parse_response;
            }

            var timeout = TimeSpan.FromMilliseconds(1000);
            bool lockTaken = false;
            detail = "unknow";
            try
            {
                Monitor.TryEnter(lockListSessionPlay, timeout, ref lockTaken);
                if (lockTaken)
                {
                    //check session is exits in _listSessionPlay
                    sessionIndex = _listSessionPlay.FindIndex(x => x.session == s.session);
                }
                else
                {
                    // The lock was not acquired.
                    _log.LogError("Can't access listID");
                    detail = "Can't access listID";
                    //goto parse_response;
                }
            }
            catch (Exception e)
            {
                Log.Logger.Error("Exception updateListSesionPlay_1: {0}", e.Message);
                //detail = "server create session fail";
                //goto parse_response;
            }
            finally
            {
                // Ensure that the lock is released.
                if (lockTaken)
                {
                    Monitor.Exit(lockListSessionPlay);
                }
            }

            if (sessionIndex == -2) //cant't access listSession to find
                goto parse_response;

            //sessionExist = _listSessionPlay.Any(x => x.session == s.session);
            if (sessionIndex >= 0)
            {
                sessionExist = true;
            }
            if (s.action == "new" && s.state == SessionPlay.StateSession.play && !sessionExist
                && File.Exists(Path.Combine(Program.mp3_dir, s.session))) //check file exits
            {
                try
                {
                    s.mp3Song = new FileStream(Path.Combine(Program.mp3_dir, s.session), FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (s.mp3Song != null)
                        s.mp3Read = new MP3_Frame_CBR(s.mp3Song);

                    if (!s.mp3Read.CheckValidMP3(2, 48, 24000))
                    {
                        detail = "Song is invalid format";
                    }
                    else
                    {
                        //_listSessionPlay.Add(s);
                        doUpdateSession = 1; //new
                        Random rd = new Random();
                        s.sessionMP3 = (UInt32)rd.Next();
                        updateFail = false;
                    }
                }
                catch (Exception e)
                {
                    Log.Logger.Error("Exception updateListSesionPlay_2: {0}", e.Message);
                    detail = "Can't open song";
                }
            }
            else if (s.action == "change" && sessionExist)
            {
                //_listSessionPlay[sessionIndex].listID.Clear();
                //_listSessionPlay[sessionIndex].listID = s.listID;
                //_listSessionPlay[sessionIndex].state = s.state;
                doUpdateSession = 2; //change
                if (s.mp3Read != null)
                {
                    //detail = mp3Read.TimePerFrame_ms.ToString() + 's';
                    updateFail = false;
                }
                else
                {
                    detail = "session mp3 crashed";
                    //_listSessionPlay[sessionIndex].state = "stop"; //use this to remove session from list
                    s.state = SessionPlay.StateSession.stop; //use this to remove session from list
                }
            }
            else
            {
                detail = "request is invalid (wrong param)";
            }

            lockTaken = false;
            if (doUpdateSession > 0)
            {
                try
                {
                    Monitor.TryEnter(lockListSessionPlay, timeout, ref lockTaken);
                    if (lockTaken)
                    {
                        if (doUpdateSession == 1)
                        {
                            _listSessionPlay.Add(s);
                        }
                        else
                        {
                            _listSessionPlay[sessionIndex].listID.Clear();
                            _listSessionPlay[sessionIndex].listID = s.listID;
                            _listSessionPlay[sessionIndex].state = s.state;
                        }
                        detail = ((long)s.mp3Read.TimePerFrame_ms * (long)s.mp3Read.TotalFrame).ToString() + "ms";
                    }
                    else
                    {
                        // The lock was not acquired.
                        _log.LogError("Can't access listID");
                        detail = "Can't access listID";
                    }
                }
                catch (Exception e)
                {
                    Log.Logger.Error("Exception updateListSesionPlay_2: {0}", e.Message);
                    //detail = "server create session fail";
                }
                finally
                {
                    // Ensure that the lock is released.
                    if (lockTaken)
                    {
                        Monitor.Exit(lockListSessionPlay);
                    }
                }
            }

        parse_response:
            if (updateFail)
                res = "fail";
            else
                res = "ok";

            //parse response to json string
            var jsonString = new
            {
                res = res,
                detail = detail
            };
            return JsonConvert.SerializeObject(jsonString);
        }

        internal string updateListSesionPlay(SessionPlay s)
        {
            if (s != null && s.type == SessionPlay.TypeSession.mp3)
                return updateListSesioPlay_MP3(s);
            else //not support
            {
                //parse response to json string
                var jsonString = new
                {
                    res = "fail",
                    detail = "request is invalid (wrong param)"
                };
                return JsonConvert.SerializeObject(jsonString);
            }
        }

        internal void updateListID(List<Int32> listID)
        {
            var timeout = TimeSpan.FromMilliseconds(1000);
            bool lockTaken = false;

            try
            {
                Monitor.TryEnter(lockListID, timeout, ref lockTaken);
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
                    Monitor.Exit(lockListID);
                }
            }
        }

        //check device is exist in listID
        internal bool checkDeviceExist(Int32 id)
        {
            if(_listID != null && 
                _listID.Any(x => x == id))
                return true;
            return false;
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

            int NUM_OF_FRAME_SEND_PER_PACKET = Program.frames_per_packet;

            CountdownEvent _countdown = new CountdownEvent(1);
            double intervalSend = NUM_OF_FRAME_SEND_PER_PACKET * Program.time_per_frame;
            int _countdownTimeout = (int)intervalSend;
            System.Timers.Timer sendTimer = new System.Timers.Timer(intervalSend);
            InitiliazeSendTimer(sendTimer, _countdown, intervalSend);
            var timeout = TimeSpan.FromMilliseconds(10);
            bool lockTaken = false, madePacketMp3 = false;

            long curTimeMs;
            long playTimeDelayms = 1000;

            while (true)
            {
                madePacketMp3 = false;
                curTimeMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                try
                {
                    Monitor.TryEnter(lockListSessionPlay, timeout, ref lockTaken);
                    if (lockTaken)
                    {
                        //make packetMp3 and send to queue of each device
                        foreach (SessionPlay ss in _listSessionPlay)
                        {
                            if(ss.state == SessionPlay.StateSession.stop || 
                                ss.state == SessionPlay.StateSession.none)
                            {
                                
                                _listSessionStop.Add(ss);
                                _listSessionPlay.Remove(ss);
                            }
                            else if (ss.type == SessionPlay.TypeSession.mp3)
                            {
                                //PrepareMP3PackAssync, and push to queue of each device
                                ss.mp3GenPlay(NUM_OF_FRAME_SEND_PER_PACKET, curTimeMs + playTimeDelayms, curTimeMs);
                            }
                        }
                        madePacketMp3 = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Logger.Error("Exception read list session in deviceServer.Run: {0}", e.Message);
                    //detail = "server create session fail";
                }
                finally
                {
                    // Ensure that the lock is released.
                    if (lockTaken)
                    {
                        Monitor.Exit(lockListSessionPlay);
                        lockTaken = false;
                    }
                }

                if (madePacketMp3)
                {
                    //send packetMp3 from queue to every device
                    //SendMP3PackAssync();
                    foreach (var session in Sessions.Values)
                    {
                        var dv = (DeviceSession)session;
                        dv.SendMP3PackAssync();
                    }
                }

                //wait until next cycle
                bool res = _countdown.Wait(_countdownTimeout); //wait after interval
                if (res) _countdown.Reset();
            }

            //while (true)
            //{
            //    //send 
            //    foreach(var song in soundList)
            //    {
            //        //long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            //        using (FileStream mp3Song = new FileStream(song, FileMode.Open, FileAccess.Read, FileShare.Read))
            //        {
            //            MP3_Frame_CBR mp3Read = new MP3_Frame_CBR(mp3Song);
            //            if (!mp3Read.CheckValidMP3(2, 48, 24000)) continue;

            //            //Stopwatch sendWatch = new Stopwatch();
            //            //sendWatch.Start();
            //            CountdownEvent _countdown = new CountdownEvent(1);
            //            double intervalSend = NUM_OF_FRAME_SEND_PER_PACKET * mp3Read.TimePerFrame_ms;
            //            int _countdownTimeout = 2 * (int)intervalSend;
            //            System.Timers.Timer sendTimer = new System.Timers.Timer(intervalSend);
            //            InitiliazeSendTimer(sendTimer, _countdown, intervalSend);

            //            int sendTime = 0;
            //            int playTimeDelayms = 1000; //client delay play 1s with server play
            //            UInt32 frameID = 0, oldFrameID = 0;
            //            //read frame and send
            //            //Stream fMp3 = File.OpenWrite(@"D:/adu.mp3");
            //            while(true)
            //            {
            //                //get frame
            //                int totalLen = 0;
            //                byte[] tmp;
            //                List<byte[]> mp3FrameList = new List<byte[]>();
            //                for (int i = 0; i < NUM_OF_FRAME_SEND_PER_PACKET; i++)
            //                {
            //                    if(i == 0)
            //                    {
            //                        //debug
            //                        //tmp = mp3Read.ReadNextADU();
            //                        tmp = mp3Read.ReadNextFrame(true);
            //                    }
            //                    else
            //                    {
            //                        tmp = mp3Read.ReadNextFrame(false);
            //                    }
            //                    if (tmp == null) break;
            //                    totalLen += tmp.Length;
            //                    mp3FrameList.Add(tmp);
            //                    frameID++;
            //                }
            //                if(totalLen > 0)
            //                {
            //                    long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            //                    //long frameTimestamp = sendTime * (long)intervalSend + startTime + playTimeDelayms;
            //                    long frameTimestamp = startTime + playTimeDelayms;
            //                    byte[] sendBuff= MP3PacketHeader.Packet(mp3FrameList, 100, frameTimestamp, oldFrameID, (UInt16)(mp3Read.Frame_size - 4), (byte)mp3Read.TimePerFrame_ms, totalLen);

            //                    //byte[] aduPac = new byte[5 * 144];
            //                    //System.Buffer.BlockCopy(mp3FrameList[0], 0, aduPac, 0, 144);
            //                    //for(int jd = 1; jd < 5; jd++)
            //                    //{
            //                    //    System.Buffer.BlockCopy(mp3FrameList[0], 0, aduPac, jd * 144, 4);
            //                    //    System.Buffer.BlockCopy(mp3FrameList[jd], 0, aduPac, jd * 144 + 4, 140);
            //                    //}

            //                    //UInt16 chsum = caculateChecksum(aduPac, 0, aduPac.Length);
            //                    //Console.WriteLine("{0} 0x{1:X}", oldFrameID, chsum);

            //                    //fMp3.Write(aduPac, 0, aduPac.Length);
            //                    //send packet
            //                    //foreach(var dv in listDeviceSession)
            //                    //{
            //                    //    dv.SendMP3PackAssync(sendBuff, 1, "bom", curTime, MP3PacketHeader.HEADER_NOENCRYPT_SIZE);
            //                    //    //dv.SendPackAssync(sendBuff, sendBuff.Length, DeviceSession.SendPackeTypeEnum.PacketMP3);
            //                    //}

            //                    // Multicast data to all sessions
            //                    foreach (var session in Sessions.Values)
            //                    {
            //                        var dv = (DeviceSession)session;
            //                        //dv.SendMP3PackAssync(sendBuff, 1, "bom", curTime, MP3PacketHeader.HEADER_NOENCRYPT_SIZE, false);

            //                        //debug
            //                        //int ofsset = 'z' - 'a' + 1;
            //                        //for(int i = 1; i < sendBuff.Length; i++)
            //                        //{
            //                        //    if(i < 10)
            //                        //    {
            //                        //        sendBuff[i] = (byte)('A' + i % ofsset);
            //                        //    }
            //                        //    else if (i > (sendBuff.Length - 10))
            //                        //    {
            //                        //        sendBuff[i] = (byte)('A' + i % ofsset);
            //                        //    }
            //                        //    else
            //                        //    {
            //                        //        sendBuff[i] = (byte)('a' + i % ofsset);
            //                        //    }
            //                        //}
            //                        //int len = sendBuff.Length; (BytesPending + sendPack.Length) < OptionSendBufferSize
            //                        if (dv.IsHandshaked)
            //                        {
            //                            //if ((dv.BytesPending + sendBuff.Length) < dv.OptionSendBufferSize)
            //                            //{
            //                            //    dv.SendAsync(BitConverter.GetBytes(sendBuff.Length));
            //                            //    System.Buffer.BlockCopy(BitConverter.GetBytes(order), 0, sendBuff, 0, 4);
            //                            //    order++;
            //                            //    dv.SendAsync(sendBuff);
            //                            //    int tmpL = sendBuff.Length;
            //                            //    _log.LogInformation($"MP3 {order}: {sendBuff[4]} {sendBuff[5]} {sendBuff[tmpL - 2]} {sendBuff[tmpL - 1]}");
            //                            //}
            //                            //else
            //                            //{
            //                            //    _log.LogInformation($"{dv.Token} miss");
            //                            //}
            //                            dv.SendMP3PackAssync(sendBuff, 1, "bom", frameTimestamp);
            //                        }
            //                    }
            //                }
            //                else
            //                {
            //                    break;
            //                }
            //                oldFrameID = frameID;
            //                //dipose
            //                mp3FrameList.Clear();
            //                bool res = _countdown.Wait(_countdownTimeout); //wait after interval
            //                if (res) _countdown.Reset();

            //                //debug
            //                //long offsetInterval = DateTimeOffset.Now.ToUnixTimeMilliseconds() - (sendTime * (long)intervalSend + startTime);
            //                //if(offsetInterval > 100 || _countdown.CurrentCount != 1 || !res)
            //                //    _log.LogInformation($"Offset time: {offsetInterval} {_countdown.CurrentCount} {res}");
            //                sendTime ++;
            //            }
            //            sendTimer.Close();
            //            _countdown.Dispose();
            //            //fMp3.Close();
            //            Thread.Sleep(500); //gap between songs
            //        }
            //    }
            //}
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

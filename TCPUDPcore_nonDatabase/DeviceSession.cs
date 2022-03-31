using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using System;
using Security;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Sockets;
using MP3_ADU;

namespace UDPTCPcore
{
    class DeviceSession : TLSSession
    {
        internal string DeviceName { get; private set; }
        private readonly ILogger<DeviceSession> _log;
        internal bool bNeedRemove = false;
        public DeviceSession(TcpServer server, ILogger<DeviceSession> log) : base(server, log)
        {
            _log = log;
        }

        internal int TokenLen { get => tokenLen; }
        internal string Token { get => token; }
        internal int ID { get => _id; }

        protected override bool CheckTokenClient()
        {
            if (((DeviceServer)Server).checkDeviceExist(_id))
                return true;
            return false;
        }

        protected override void OnTLSConnectedNotify()
        {
            //DeviceServer deviceServer = Program.host.Services.GetRequiredService<DeviceServer>();
            //remove duplicate device
            //var dvIndx = deviceServer.listDeviceSession.FindLastIndex(dv => dv.token == token);
            //if (dvIndx != -1)
            //    deviceServer.listDeviceSession[dvIndx].bNeedRemove = true;
            //then add to server
            //deviceServer.listDeviceSession.Add(this);
        }

        protected override void OnTLSDisConnectedNotify()
        {
            //DeviceServer deviceServer = Program.host.Services.GetRequiredService<DeviceServer>();
            //var dvIndx = deviceServer.listDeviceSession.FindLastIndex(dv => dv.token == token);
            //if (dvIndx != -1)
            //    deviceServer.listDeviceSession[dvIndx].bNeedRemove = true;
            //deviceServer.listDeviceSession.Remove(this);
            //_log.LogError($"{Id} disconnect!");
        }

        // 1, 2, 3, 4
        internal enum RecvPackeTypeEnum { Status, PacketAudio};
        bool ErrorRecv = false;
        protected override bool HandleTLSPacket()
        {
            return true;
            //int packetType = (int)Tcpbuff[POS_OF_BYTE_TYPE];

            //if (IsHandshaked) 
            //{
            //    //check type of packet
            //    if(packetType == (int)RecvPackeTypeEnum.Status)
            //    {
            //        //get status signalQuality(=0, if ethernet), Position (GPS), LostPacketRecvMP3
            //    }
            //    else if(packetType == (int)RecvPackeTypeEnum.PacketAudio)
            //    {
            //        //put to queue audio + Seri (Device ID)
            //    }
            //    else //somthing wrong, disconnect
            //    {
            //        ErrorRecv = true;
            //    }
            //}
            //else
            //{
            //    if(packetType == (int)RecvPackeTypeEnum.AESkey_ID)
            //    {
            //        //decrypt RSA and then get ID Device and check, if exist, then add to listDevice
            //        //before add to listDevice, check IsDuplicate, if has, remove old Device (remove in server.Session, and in listDevice)
            //        //then get AES-128 key
            //        byte[] plainTextRSA = new byte[curPacketSize];
            //        System.Buffer.BlockCopy(Tcpbuff, 0, plainTextRSA, 0, curPacketSize);
            //        byte[] decrypted = rsa.Decrypt(plainTextRSA);
            //        if(decrypted != null)
            //        {
            //            //get first 24B ID
            //            Seri = Encoding.UTF8.GetString(decrypted, 0, SeriLen);
            //            //check Seri is exist
            //            if(true)
            //            {
            //                //get next 16B AES
            //                AESkey = new byte[AESkeyLen];
            //                System.Buffer.BlockCopy(decrypted, SeriLen, AESkey, 0, AESkeyLen);
            //                //send back ID to ACK with client
            //                SendPackAssync(null, 0, SendPackeTypeEnum.ACK);
            //                IsHandshaked = true;
            //            }
            //            ErrorRecv = true;
            //        } 
            //        else
            //        {
            //            ErrorRecv = true;
            //        }

            //    }
            //    else
            //    {
            //        ErrorRecv = true;
            //    }
            //}
            //if(ErrorRecv)
            //{
            //    DeviceDisconnect();
            //}
        }

        //session: aes-128. Each time send audio, ex: a song, will create new aes-128, then encypt header
        //with this. Each device don't need re-encrypt before send 
        //ACK, send back Seri received to client to finish handshake
        //1 2 3 4 5
        internal enum SendTLSPackeTypeEnum { Status, PacketMP3 };

        int curSendMp3Priority = 0;
        string curUserSend = null;
        UInt32 curSession = 0;
        long lastSendTimestampe = 0; // ms, UnixTimeMilliseconds
        const int sessionTimeout = 1000; //1s

        //notify to this task that user with this priority finished sending
        internal void SendMP3PackAssyncRelease(int priority, string userSend)
        {
            if(priority == curSendMp3Priority && userSend == curUserSend)
            {
                curSendMp3Priority = 0;
                curUserSend = null;
            }
        }
        int missFrame = 0, countSend = 0;
        long totalBytes = 0;
        byte[] sendBuff;

        //push byte array to device buffer
        internal void PrepareMP3PackAssync(byte[] sendPack, int priority, long sendTimestamp)
        {
            if (sendPack == null || (!IsHandshaked)) return;

            if ((sendTimestamp - lastSendTimestampe) > sessionTimeout)
            {
                //timeout, reset new session
                curUserSend = null;
                curSendMp3Priority = 0;
            }

            if (priority > curSendMp3Priority)
            {
                curSendMp3Priority = priority;
            }

            if (priority == curSendMp3Priority)
            {
                sendBuff = sendPack;
                lastSendTimestampe = sendTimestamp;
            }
        }

        internal void SendMP3PackAssync()
        {

            if (sendBuff != null)
            {
                if (!SendTLSPacket(sendBuff, false))
                {
                    missFrame++;
                    _log.LogInformation($"{Id} {token} miss frame: {missFrame}. Sending {BytesSending}, pending {BytesPending}, Size {OptionSendBufferSize}");
                }
                else
                {
                    totalBytes += (sendBuff.Length * 2 + 1);
                    //Console.Write($"{token[tokenLen-1]} ({0}).", totalBytes);
                }
                sendBuff = null;
            }
        }
    }
}

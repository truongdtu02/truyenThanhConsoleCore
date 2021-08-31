using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using System;
using Security;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Sockets;

namespace UDPTCPcore
{
    class DeviceSession : TcpSession
    {
        internal string Seri { get; private set; } //~ID of device, because it is the same with ID session
        internal string DeviceName { get; private set; }
        internal bool IsHandshaked { get; private set; } //get successfull AES key
        RSA rsa;
        internal long ConnectedTime { get; private set; }
        private readonly ILogger<DeviceSession> _log;
        byte[] AESkey;
        public DeviceSession(TcpServer server, ILogger<DeviceSession> log) : base(server) 
        {
            IsHandshaked = false;
            ConnectedTime = 0;
            rsa = new RSA();
            _log = log;
        }

        protected override void OnConnected()
        {
            ConnectedTime = DateTimeOffset.Now.ToUnixTimeSeconds();
            _log.LogInformation($"{DateTimeOffset.Now}, Id {Id} connected!");

            //Send pubkey
            SendPackAssync(null, 0, SendPackeTypeEnum.Pubkey);

            //add to list
            DeviceServer deviceServer = Program.host.Services.GetRequiredService<DeviceServer>();
            deviceServer.listDeviceSession.Add(this);
        }

        protected override void OnDisconnected()
        {
            _log.LogInformation($"Id {Id}, Dev {DeviceName} disconnected!");

            DeviceServer deviceServer = Program.host.Services.GetRequiredService<DeviceServer>();
            deviceServer.listDeviceSession.Remove(this);
        }

        const int SeriLen = 24; //24B
        const int AESkeyLen = 16;
        const int TCP_BUFF_LEN_MAX = 9000;
        const int TCP_HEADER_LEN = 4; //4B len of packet, //16B MD5, 1B type
        const int POS_OF_MD5 = 4;
        const int POS_OF_BYTE_TYPE = 20; //position of byte indicate type of packet, pos count from 0
        byte[] headerBuff = new byte[TCP_HEADER_LEN];
        byte[] Tcpbuff;
        int TcpbuffOffset = 0;

        bool bIsPending = false;
        bool bIgnore = false; //ignore when tcp packet length > mem
        int remainData = 0;   //reamin data need to collect
        int curPacketSize;
        //int totalByteLength = 0;

        void DeviceDisconnect()
        {
            //first remove from list devices of server
            //???
            //then
            Disconnect();
        }
        //first we wil store 4B len and 1B type to Tcpbuff, to read len and type
        // then playload of packet we will overwrite or write at the begginning of Tcpbuff
        void AnalyzeRecvTcpPacket(byte[] recvBuff, int offset, int length)
        {
            while (length > 0)
            {
                //beggin get length of packet
                if (!bIsPending)
                {
                    //barely occur, not enough data to detect lenght of TCP packet
                    if ((TcpbuffOffset + length) < TCP_HEADER_LEN)
                    {
                        System.Buffer.BlockCopy(recvBuff, offset, headerBuff, TcpbuffOffset, length);
                        length = 0;
                        TcpbuffOffset += length;
                    }
                    //else enough data to detect
                    else
                    {
                        //copy just enough
                        int tmpOffset = TCP_HEADER_LEN - TcpbuffOffset;
                        System.Buffer.BlockCopy(recvBuff, offset, headerBuff, TcpbuffOffset, tmpOffset);
                        TcpbuffOffset = 0; //reset to 0 to copy payload
                        length -= tmpOffset;
                        offset += tmpOffset;
                        bIsPending = true;

                        remainData = BitConverter.ToInt32(Tcpbuff, 0);
                        curPacketSize = remainData;

                        Tcpbuff = new byte[curPacketSize];

                        //not enough mem, so just ignore or disconnect, since something wrong
                        if(remainData > TCP_BUFF_LEN_MAX || remainData < 0)
                        {
                            DeviceDisconnect();
                        }

                    }
                }
                //got length, continue collect data
                else
                {
                    //ignore save to buff
                    if (bIgnore)
                    {
                        if (length < remainData)
                        {
                            remainData -= length;
                            length = 0;
                        }
                        else
                        {
                            //done packet
                            length -= remainData;
                            bIsPending = false;
                        }
                    }
                    //save to buff
                    else
                    {
                        //not enough data to get
                        if (length < remainData)
                        {
                            System.Buffer.BlockCopy(recvBuff, offset, Tcpbuff, TcpbuffOffset, length);
                            TcpbuffOffset += length;
                            remainData -= length;
                            length = 0; //handled all data in tcpPacket
                        }
                        else
                        {
                            //done packet
                            System.Buffer.BlockCopy(recvBuff, offset, Tcpbuff, TcpbuffOffset, remainData);
                            length -= remainData;
                            offset += remainData;
                            //reset
                            bIsPending = false;
                            TcpbuffOffset = 0; 
                        }
                    }

                    //that mean get done a packet
                    if (!bIsPending)
                    {
                        HandleRecvTcpPacket();
                    }
                }
            }
        }

        bool CompareMD5(byte[] inputData, int offsetData, int countData, byte[] inputMD5, int offsetMD5)
        {
            byte[] hashed = MD5.MD5Hash(inputData, offsetData, countData);
            //CompareMD5 (16B)
            for(int i = 0; i < 16; i++)
            {
                if (hashed[i] != inputMD5[offsetMD5 + i]) return false;
            }
            return true;
        }

        // 1, 2, 3, 4
        internal enum RecvPackeTypeEnum { KeepAlive, Status, PacketAudio, AESkey_ID };
        bool ErrorRecv = false;
        void HandleRecvTcpPacket()
        {
            //at least 16B MD5, 1B type, 1B data
            if (curPacketSize < 18) return; //keep alive
            //check MD5 first
            if (!CompareMD5(Tcpbuff, POS_OF_BYTE_TYPE, curPacketSize - TCP_HEADER_LEN - 16, Tcpbuff, POS_OF_MD5)) return; //wrong data
            //record recv time
            ConnectedTime = DateTimeOffset.Now.ToUnixTimeSeconds();

            int packetType = (int)Tcpbuff[POS_OF_BYTE_TYPE];

            if (IsHandshaked) 
            {
                //check type of packet
                if(packetType == (int)RecvPackeTypeEnum.Status)
                {
                    //get status signalQuality(=0, if ethernet), Position (GPS), LostPacketRecvMP3
                }
                else if(packetType == (int)RecvPackeTypeEnum.PacketAudio)
                {
                    //put to queue audio + Seri (Device ID)
                }
                else //somthing wrong, disconnect
                {
                    ErrorRecv = true;
                }
            }
            else
            {
                if(packetType == (int)RecvPackeTypeEnum.AESkey_ID)
                {
                    //decrypt RSA and then get ID Device and check, if exist, then add to listDevice
                    //before add to listDevice, check IsDuplicate, if has, remove old Device (remove in server.Session, and in listDevice)
                    //then get AES-128 key
                    byte[] plainTextRSA = new byte[curPacketSize];
                    System.Buffer.BlockCopy(Tcpbuff, 0, plainTextRSA, 0, curPacketSize);
                    byte[] decrypted = rsa.Decrypt(plainTextRSA);
                    if(decrypted != null)
                    {
                        //get first 24B ID
                        Seri = Encoding.UTF8.GetString(decrypted, 0, SeriLen);
                        //check Seri is exist
                        if(true)
                        {
                            //get next 16B AES
                            AESkey = new byte[AESkeyLen];
                            System.Buffer.BlockCopy(decrypted, SeriLen, AESkey, 0, AESkeyLen);
                            //send back ID to ACK with client
                            SendPackAssync(null, 0, SendPackeTypeEnum.ACK);
                            IsHandshaked = true;
                        }
                        ErrorRecv = true;
                    } 
                    else
                    {
                        ErrorRecv = true;
                    }

                }
                else
                {
                    ErrorRecv = true;
                }
            }
            if(ErrorRecv)
            {
                DeviceDisconnect();
            }
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            //string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
            //Console.WriteLine("Incoming: " + message);
            _log.LogInformation($"Recv, ID {Id}, Dev: {DeviceName}, len: {size}");

            AnalyzeRecvTcpPacket(buffer, (int)offset, (int)size);
        }

        protected override void OnError(SocketError error)
        {
            _log.LogError($"Chat TCP session caught an error with code {error}");
        }

        protected override void OnEmpty()
        {
            //Console.WriteLine($"S {++countSend} {DateTimeOffset.Now.ToUnixTimeSeconds() - ConnectedTime} ");
            //Console.WriteLine($"ID {Id}, Pending byte: {BytesPending}, Sending bytes: {BytesSending}, Sent bytes: {BytesSent}");
        }
        
        //session: aes-128. Each time send audio, ex: a song, will create new aes-128, then encypt header
        //with this. Each device don't need re-encrypt before send 
        //ACK, send back Seri received to client to finish handshake
        //1 2 3 4 5
        internal enum SendPackeTypeEnum { None, Pubkey,  Session, Status, PacketMP3, ACK };

        internal void SendPackAssync(byte[] sendPack, int len, SendPackeTypeEnum type)
        {
            if (!IsHandshaked)
            {
                if (type == SendPackeTypeEnum.Pubkey)
                {
                    byte[] sendPubkeyBuff = new byte[TCP_HEADER_LEN + rsa.publicKey.Modulus.Length]; //4 byte length , 1 byte type, n byte pub key
                    System.Buffer.BlockCopy(BitConverter.GetBytes(rsa.publicKey.Modulus.Length), 0, sendPubkeyBuff, 0, 4);
                    sendPubkeyBuff[POS_OF_BYTE_TYPE] = (byte)type; //type
                    //copy pubkey
                    System.Buffer.BlockCopy(rsa.publicKey.Modulus, 0, sendPubkeyBuff, POS_OF_BYTE_TYPE + 1, rsa.publicKey.Modulus.Length);
                    //trick exchange first and last two bytes to prevent hack
                    byte tmp = sendPubkeyBuff[POS_OF_BYTE_TYPE + 1];
                    sendPubkeyBuff[POS_OF_BYTE_TYPE + 1] = sendPubkeyBuff[POS_OF_BYTE_TYPE + 2];
                    sendPubkeyBuff[POS_OF_BYTE_TYPE + 2] = tmp;

                    tmp = sendPubkeyBuff[sendPubkeyBuff.Length - 2];
                    sendPubkeyBuff[sendPubkeyBuff.Length - 2] = sendPubkeyBuff[sendPubkeyBuff.Length - 1];
                    sendPubkeyBuff[sendPubkeyBuff.Length - 1] = tmp;

                    SendAsync(sendPubkeyBuff);
                }
                else if (type == SendPackeTypeEnum.ACK)
                {
                    byte[] header = new byte[TCP_HEADER_LEN]; //4 byte length , 1 byte type
                    //encrypt paket before send, to ACK, send back Seri
                    byte[] encrypted = AES.AES_Encrypt(Encoding.UTF8.GetBytes(Seri), 0, Seri.Length, AESkey);
                    System.Buffer.BlockCopy(BitConverter.GetBytes(encrypted.Length), 0, header, 0, 4);
                    header[POS_OF_BYTE_TYPE] = (byte)type;
                    SendAsync(header);
                    SendAsync(encrypted);
                }
            }
            else
            {
                if (type == SendPackeTypeEnum.Session || type == SendPackeTypeEnum.Status)
                {
                    if (sendPack == null) return;
                    byte[] header = new byte[TCP_HEADER_LEN]; //4 byte length , 1 byte type
                    //encrypt paket before send
                    byte[] encrypted = AES.AES_Encrypt(sendPack, 0, len, AESkey);
                    System.Buffer.BlockCopy(BitConverter.GetBytes(encrypted.Length), 0, header, 0, 4);
                    header[POS_OF_BYTE_TYPE] = (byte)type;
                    SendAsync(header);
                    SendAsync(encrypted);
                }
            }
        }

        int curSendMp3Priority = 0;
        string curUserSend;
        UInt32 curSession = 0;
        long lastSendTimestampe = 0; // ms, UnixTimeMilliseconds
        const int sessionTimeout = 3000; //3s
        internal void SendMP3PackAssync(byte[] sendPack, int len, int priority, string userSend, long sendTimestamp)
        {
            if (sendPack == null) return;

            if((DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastSendTimestampe) > sessionTimeout)
            {
                //timeout, reset new session
                curUserSend = null;
                curSendMp3Priority = 0;
            }

            if((priority > curSendMp3Priority) || ((priority == curSendMp3Priority) && (userSend != curUserSend)))
            {
                curSendMp3Priority = priority;
                curUserSend = userSend;
                curSession++;
            }

            if(priority == curSendMp3Priority)
            {
                byte[] header = new byte[TCP_HEADER_LEN]; //4 byte length , 1 byte type

                //copy session
                System.Buffer.BlockCopy(BitConverter.GetBytes(curSession), 0, sendPack, 0, sizeof(UInt32));

                byte[] encrypted = AES.AES_Encrypt(sendPack, 0, len, AESkey);

                //put len
                header[POS_OF_BYTE_TYPE] = (byte)SendPackeTypeEnum.PacketMP3;
                SendAsync(header);
                SendAsync(sendPack);

                lastSendTimestampe = sendTimestamp;
            }
        }
    }
}

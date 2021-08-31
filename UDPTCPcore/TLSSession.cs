using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using System;
using Security;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Sockets;
using System.Timers;

namespace UDPTCPcore
{
    class TLSSession : TcpSession
    {
        const int CONNECT_TIMEOUT = 120000; //120s
        internal bool IsHandshaked { get; private set; } //get successfull AES key
        RSA rsa;
        System.Timers.Timer timeoutTimer;
        internal long ConnectedTime { get; private set; }
        private readonly ILogger<DeviceSession> _log;
        byte[] AESkey;
        const int AESkeyLen = 16;

        byte[] salt;
        //int saltLen; //== tokenLen
        internal string Token { get; private set; } //~ID of device
        struct TcpPacketStruct
        {
            public const int SIZE_OF_LEN = 4; //4B len of packet, //16B MD5
            public const int POS_OF_MD5 = 0; //right after len filed
            public const int SIZE_OF_MD5 = 16;
            public const int POS_OF_PAYLOAD = 16; //position of payload of packet

            public const int HEADER_LEN = SIZE_OF_LEN + SIZE_OF_MD5;
        }
        byte[] headerBuff = new byte[TcpPacketStruct.SIZE_OF_LEN];
        byte[] Tcpbuff;
        int TcpbuffOffset = 0;

        bool bIsPending = false;
        bool bIgnore = false; //ignore when tcp packet length > mem
        int remainData = 0;   //reamin data need to collect
        int curPacketSize;
        bool ErrorRecv = false;

        //** protected properties
        protected int TokenLen;
        protected int TCP_BUFF_LEN_MAX;

        //** virtual methods, need to override
        //derived can use to do something when client TLS handshake is successful, like add to list
        protected virtual void OnTLSConnectedNotify()
        {

        }

        //derived can use to do something when client disconnected, like remove from list
        protected virtual void OnTLSDisConnectedNotify()
        {

        }

        //handle TLS packet (after TLS handshake) at derived class
        protected virtual void HandleTLSPacket()
        {

        }

        //check token at derived class
        protected virtual bool CheckTokenClient() { return true; }

        //record time connected after TLS handshake to database (each time recv packet)
        protected virtual void RecordTimeConnectedToDatabase() { }

        public TLSSession(TcpServer server, ILogger<DeviceSession> log) : base(server)
        {
            IsHandshaked = false;
            rsa = new RSA();
            _log = log;

            InitiliazeTimeoutTimer();
        }

        void InitiliazeTimeoutTimer()
        {
            // Create a timer to handle connect time-out
            timeoutTimer = new System.Timers.Timer(CONNECT_TIMEOUT);
            // Hook up the Elapsed event for the timer. 
            timeoutTimer.Elapsed += ConnectTimeoutEvent;
            timeoutTimer.AutoReset = true;
            timeoutTimer.Enabled = true;
        }

        void ResetTimeoutTimer()
        {
            timeoutTimer.Interval = CONNECT_TIMEOUT;
        }

        private void ConnectTimeoutEvent(Object source, ElapsedEventArgs e)
        {
            _log.LogInformation($"{Id} timeout.");
            Disconnect();
        }

        protected override void OnConnected()
        {
            //ConnectedTime = DateTimeOffset.Now.ToUnixTimeSeconds();
            _log.LogInformation($"{Id} connected!");

            //Send pubkey
            SendHandshakePackAsync(SendPackeTypeEnum.Pubkey);
        }

        protected override void OnDisconnected()
        {
            timeoutTimer.Close(); //release timer to make sure don't double Disconnect();

            _log.LogInformation($"{Id} disconnected!");

            OnTLSDisConnectedNotify();
        }

        //first we wil store length filed (4B) to read len of packet
        void AnalyzeRecvTcpPacket(byte[] recvData, int offset, int length)
        {
            while (length > 0)
            {
                //beggin get length of packet
                if (!bIsPending)
                {
                    //barely occur, not enough data to detect lenght of TCP packet
                    if ((TcpbuffOffset + length) < TcpPacketStruct.SIZE_OF_LEN)
                    {
                        System.Buffer.BlockCopy(recvData, offset, headerBuff, TcpbuffOffset, length);
                        length = 0;
                        TcpbuffOffset += length;
                    }
                    //else enough data to detect
                    else
                    {
                        //copy just enough
                        int tmpOffset = TcpPacketStruct.SIZE_OF_LEN - TcpbuffOffset;
                        System.Buffer.BlockCopy(recvData, offset, headerBuff, TcpbuffOffset, tmpOffset);
                        TcpbuffOffset = 0; //reset to 0 to copy payload
                        length -= tmpOffset;
                        offset += tmpOffset;
                        bIsPending = true;

                        remainData = BitConverter.ToInt32(Tcpbuff, 0);
                        curPacketSize = remainData;

                        Tcpbuff = new byte[curPacketSize];

                        //not enough mem, so just ignore or disconnect, since something wrong
                        if (remainData > TCP_BUFF_LEN_MAX || remainData < 0)
                        {
                            Disconnect();
                        }

                    }
                }
                //got length, continue collect data
                else
                {
                    //save to buff
                    //not enough data to get
                    if (length < remainData)
                    {
                        System.Buffer.BlockCopy(recvData, offset, Tcpbuff, TcpbuffOffset, length);
                        TcpbuffOffset += length;
                        remainData -= length;
                        length = 0; //handled all data in tcpPacket
                    }
                    else
                    {
                        //done packet
                        System.Buffer.BlockCopy(recvData, offset, Tcpbuff, TcpbuffOffset, remainData);
                        length -= remainData;
                        offset += remainData;
                        //reset
                        bIsPending = false;
                        TcpbuffOffset = 0;
                    }

                    //that mean get done a packet
                    if (!bIsPending)
                    {
                        HandleRecvTcpPacket();
                        //dipose array byte
                        Tcpbuff = null;
                    }
                }
            }
        }

        bool CheckMD5(byte[] inputData, int offsetData, int countData, byte[] inputMD5, int offsetMD5)
        {
            //check AES
            if (AESkey == null) //it is first packet AES_ID_salt, not necessary check MD5, RSA is enough
                return true;

            //decrypt MD5 first
            byte[] MD5checksum = AES.AES_Decrypt(inputMD5, offsetMD5, TcpPacketStruct.SIZE_OF_MD5, AESkey, true); //overwrite
            if (MD5checksum == null) return false;

            byte[] hashed = MD5.MD5Hash(inputData, offsetData, countData);
            //CompareMD5 (16B)
            for (int i = 0; i < TcpPacketStruct.SIZE_OF_MD5; i++)
            {
                if (hashed[i] != inputMD5[offsetMD5 + i]) return false;
            }
            return true;
        }

        //IsPlus = true when we want add salt with text, otherwise input data will be subtract with salt to make original text
        enum SaltEnum { Add, Sub};
        void ConvertTextWithSalt(byte[] data, int offset, int len, SaltEnum saltType)
        {
            if (salt == null) return;
            int i = 0;
            if(saltType == SaltEnum.Add)
            {
                while (len > 0)
                {
                    data[i + offset] += salt[i];
                    len--;
                    i++;
                    if (i == salt.Length) i = 0;
                }
            }
            else //sub
            {
                while (len > 0)
                {
                    data[i + offset] -= salt[i];
                    len--;
                    i++;
                    if (i == salt.Length) i = 0;
                }
            }
        }

        void HandleRecvTcpPacket()
        {
            //at least 16B MD5, 1B data
            if (curPacketSize > 16)
            {
                //check MD5 first
                if (!CheckMD5(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD, curPacketSize - TcpPacketStruct.SIZE_OF_MD5, Tcpbuff, TcpPacketStruct.POS_OF_MD5)) return; //wrong data

                if (IsHandshaked)
                {
                    HandleTLSPacket();  //handle at derived class
                }
                else
                {
                    //decrypt RSA packet, get salt, check Token and then get AES-128
                    byte[] decrypted = rsa.Decrypt(Tcpbuff);
                    if (decrypted != null)
                    {
                        //get first salt
                        salt = new byte[TokenLen];
                        System.Buffer.BlockCopy(decrypted, TcpPacketStruct.POS_OF_PAYLOAD, salt, 0, TokenLen);
                        //get Token, but de-convert with salt first
                        ConvertTextWithSalt(decrypted, TcpPacketStruct.POS_OF_PAYLOAD + salt.Length, TokenLen, SaltEnum.Sub);
                        Token = Encoding.UTF8.GetString(decrypted, TcpPacketStruct.POS_OF_PAYLOAD + salt.Length, TokenLen);
                        //check Token is exist
                        if (CheckTokenClient())
                        {
                            //get next 16B AES
                            AESkey = new byte[AESkeyLen];
                            System.Buffer.BlockCopy(decrypted, TcpPacketStruct.POS_OF_PAYLOAD + salt.Length + TokenLen, AESkey, 0, AESkeyLen);

                            //send back ID to ACK with client
                            SendHandshakePackAsync(SendPackeTypeEnum.ACK);
                            IsHandshaked = true;
                        }
                        else 
                            ErrorRecv = true;
                    }
                    else
                    {
                        ErrorRecv = true;
                    }
                }
            } 
            else { } //keep alive, nothing can't do    
            
            
            if (ErrorRecv)
            {
                Disconnect();
            }
            else
            {
                ResetTimeoutTimer();
                //record recv time to database
                ConnectedTime = DateTimeOffset.Now.ToUnixTimeSeconds();
                RecordTimeConnectedToDatabase();
            }
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            //string message = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
            //Console.WriteLine("Incoming: " + message);
            _log.LogInformation($"{Id} Recv_len: {size}");

            AnalyzeRecvTcpPacket(buffer, (int)offset, (int)size);
        }

        protected override void OnError(SocketError error)
        {
            _log.LogError($"Chat TCP session caught an error with code {error}");
        }

        protected override void OnEmpty()
        {
            //Console.WriteLine($"ID {Id}, Pending byte: {BytesPending}, Sending bytes: {BytesSending}, Sent bytes: {BytesSent}");
        }

        //1 2 3 4 5
        internal enum SendPackeTypeEnum { None, Pubkey, ACK };

        //get length and add MD5 (then encrypt) to packet before send
        internal void SendPacketAsync(byte[] data, int offset, int len)
        {
            //check data array
            if ((data.Length - offset) < len) return;

            byte[] headerArr = new byte[TcpPacketStruct.HEADER_LEN];
            System.Buffer.BlockCopy(BitConverter.GetBytes(TcpPacketStruct.SIZE_OF_MD5 + len), 0, headerArr, 0, sizeof(int));
            if(AESkey != null)
            {
                byte[] md5Checksum = MD5.MD5Hash(data, offset, len);
                md5Checksum = AES.AES_Encrypt(md5Checksum, 0, md5Checksum.Length, AESkey); //encrypt
                System.Buffer.BlockCopy(md5Checksum, 0, headerArr, sizeof(int), TcpPacketStruct.SIZE_OF_MD5);

                //encrypt data
                byte[] encryptedData = AES.AES_Encrypt(data, offset, len, AESkey);

                //send header then payload
                SendAsync(headerArr);
                SendAsync(encryptedData);
            }
            else //this case has only one packet, that is sending pubkey
            {
                byte[] md5Checksum = MD5.MD5Hash(data, offset, len);
                System.Buffer.BlockCopy(md5Checksum, 0, headerArr, sizeof(int), TcpPacketStruct.SIZE_OF_MD5);
                SendAsync(headerArr);
                SendAsync(data, (long)offset, (long)len);
            }
        }

        void SendHandshakePackAsync(SendPackeTypeEnum type)
        {
            if (!IsHandshaked)
            {
                if (type == SendPackeTypeEnum.Pubkey)
                {
                    byte[] sendPubkeyBuff = new byte[rsa.publicKey.Modulus.Length]; //4 byte length , 1 byte type, n byte pub key
                    //copy pubkey
                    System.Buffer.BlockCopy(rsa.publicKey.Modulus, 0, sendPubkeyBuff, 0, rsa.publicKey.Modulus.Length);

                    //trick exchange first and last two bytes to prevent hack
                    byte tmp = sendPubkeyBuff[ 1];
                    sendPubkeyBuff[1] = sendPubkeyBuff[0];
                    sendPubkeyBuff[0] = tmp;

                    tmp = sendPubkeyBuff[sendPubkeyBuff.Length - 2];
                    sendPubkeyBuff[sendPubkeyBuff.Length - 2] = sendPubkeyBuff[sendPubkeyBuff.Length - 1];
                    sendPubkeyBuff[sendPubkeyBuff.Length - 1] = tmp;

                    SendPacketAsync(sendPubkeyBuff, 0, sendPubkeyBuff.Length);
                }
                else if (type == SendPackeTypeEnum.ACK)
                {
                    //send back salt
                    if(salt != null)
                    {
                        SendPacketAsync(salt, 0, salt.Length);
                    }
                }
            }
        }

    }
}

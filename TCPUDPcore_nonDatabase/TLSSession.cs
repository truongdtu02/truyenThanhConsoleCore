using Microsoft.Extensions.DependencyInjection;
using NetCoreServer;
using System;
using Security;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Sockets;
using System.Timers;
using System.Threading;
using Serilog;

namespace UDPTCPcore
{
    class TLSSession : NetCoreServer.TcpSession
    {
        //debug
        const int CONNECT_TIMEOUT = 10 * 1000; //10s for TLS handshake
        const int KEEP_ALIVE_TIMEOUT = 120 * 1000; //120s for keep alive after handshake
        internal bool IsHandshaked { get; private set; } //get successfull AES key
        public RSA rsa;
        System.Timers.Timer timeoutTimer;
        internal long ConnectedTime { get; private set; }
        private readonly ILogger<TLSSession> _log;
        protected byte[] AESkey;
        const int AESkeyLen = 16;

        byte[] salt;
        //int saltLen; //== tokenLen
        protected string token { get; set; } //~ID of device in string
        protected Int32 _id { get; set; } //~ID of device
        /* Note: this structure is suitable with received packet, when we don't get length file (4B) to Tcpbuff
         * So be carefully use this with send packet
         */
        protected struct TcpPacketStruct
        {
            public const int POS_OF_LEN = 0;
            public const int SIZE_OF_LEN = 2; //2B len of packet, //16B MD5
            public const int POS_OF_MD5 = 2; //right after len filed
            public const int SIZE_OF_MD5 = 16;
            public const int POS_OF_PAYLOAD = 18; //position of payload of packet

            public const int HEADER_LEN = SIZE_OF_LEN + SIZE_OF_MD5;
        }

        byte[] headerBuff = new byte[TcpPacketStruct.SIZE_OF_LEN];
        byte[] Tcpbuff;
        int TcpbuffOffset = 0;

        bool bIsPending = false;
        int remainData = 0;   //reamin data need to collect
        int curPacketSize;
        bool ErrorRecv = false;

        //** protected properties
        protected int tokenLen = 24;
        protected int TCP_BUFF_LEN_MAX = 10000;

        //** virtual methods, need to override
        //derived can use to do something when client TLS handshake is successful, like add to list
        protected virtual void OnTLSConnectedNotify()
        {

        }

        //derived can use to do something when client disconnected, like remove from list
        protected virtual void OnTLSDisConnectedNotify()
        {

        }

        //handle TLS packet (after TLS handshake) at derived class, return false if packet has something wrong
        protected virtual bool HandleTLSPacket()
        {
            return true;
        }

        //check token at derived class (Declared virtual so it can be overridden.)
        protected virtual bool CheckTokenClient() { return true; }

        //record time connected after TLS handshake to database (each time recv packet)
        protected virtual void RecordTimeConnectedToDatabase() { }

        public TLSSession(TcpServer server, ILogger<TLSSession> log) : base(server)
        {
            ConnectedTime = 0;
            IsHandshaked = false;
            rsa = new RSA();
            _log = log;
            OptionSendBufferSize = Program.send_buffer_size;//40000;

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

        //reset when recv TLS or keep-alive packet
        void ResetKeepAliveTimeoutTimer()
        {
            timeoutTimer.Interval = KEEP_ALIVE_TIMEOUT;
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
            //SendHandshakePackAsync(SendPackeTypeEnum.Pubkey);
        }

        protected override void OnDisconnected()
        {
            timeoutTimer.Close(); //release timer to make sure don't double Disconnect();

            _log.LogInformation($"{Id} {token} disconnected!");

            OnTLSDisConnectedNotify();
        }

        //first we wil store length field (4B) to read len of packet
        public void AnalyzeRecvTcpPacket(byte[] recvData, int offset, int length)
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
                        TcpbuffOffset += length;
                        length = 0;
                    }
                    //else enough data to detect
                    else
                    {
                        //copy just enough
                        int tmpOffset = TcpPacketStruct.SIZE_OF_LEN - TcpbuffOffset;
                        System.Buffer.BlockCopy(recvData, offset, headerBuff, TcpbuffOffset, tmpOffset);
                        TcpbuffOffset = TcpPacketStruct.SIZE_OF_LEN;
                        length -= tmpOffset;
                        offset += tmpOffset;
                        bIsPending = true;

                        remainData = (int)BitConverter.ToUInt16(headerBuff, 0); //not necessary & 0x0000FFFF;
                        curPacketSize = remainData;

                        Tcpbuff = new byte[curPacketSize + TcpPacketStruct.SIZE_OF_LEN];

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

        bool CheckMD5()
        {
            //check AES
            if (AESkey == null) //it is first packet AES_ID_salt, not necessary check MD5, RSA is enough
                return true;

            //decrypt MD5 first
            byte[] MD5checksum = AES.AES_Decrypt(Tcpbuff, TcpPacketStruct.POS_OF_MD5, TcpPacketStruct.SIZE_OF_MD5, AESkey, true); //overwrite
            if (MD5checksum == null) return false;

            byte[] hashed = MD5.MD5Hash(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD, curPacketSize - TcpPacketStruct.SIZE_OF_MD5);
            //CompareMD5 (16B)
            for (int i = 0; i < TcpPacketStruct.SIZE_OF_MD5; i++)
            {
                if (hashed[i] != Tcpbuff[TcpPacketStruct.POS_OF_MD5 + i]) return false;
            }
            return true;
        }

        //use for (salt, Token and AES) packet sent from client
        bool CheckMD5NoDecrypt()
        {
            byte[] hashed = MD5.MD5Hash(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD, curPacketSize - TcpPacketStruct.SIZE_OF_MD5);
            //CompareMD5 (16B)
            for (int i = 0; i < TcpPacketStruct.SIZE_OF_MD5; i++)
            {
                if (hashed[i] != Tcpbuff[TcpPacketStruct.POS_OF_MD5 + i]) return false;
            }
            return true;
        }

        enum SaltEnum { Add, Sub};
        void ConvertTextWithSalt(byte[] data, int offset, int len, SaltEnum saltType)
        {
            if (salt == null) return; //something wrong ???

            int i = 0, j = 0;
            if(saltType == SaltEnum.Add)
            {
                while (j < len)
                {
                    data[j + offset] += salt[i];
                    j++;
                    i++;
                    if (i == salt.Length) i = 0;
                }
            }
            else //sub
            {
                while (j < len)
                {
                    data[j + offset] -= salt[i];
                    byte tmp = data[j + offset];
                    j++;
                    i++;
                    if (i == salt.Length) i = 0;
                }
            }
        }
        int totalBytes = 0;
        void HandleRecvTcpPacket()
        {
            ErrorRecv = true;
            //at least 16B MD5, 1B data
            if (curPacketSize > 16)
            {
                //check MD5 first
                if (CheckMD5())
                {
                    if (IsHandshaked)
                    {
                        if (HandleTLSPacket())  //handle at derived class
                            ErrorRecv = false;
                    }
                    else
                    {
                        //decrypt RSA packet, get salt, check Token and then get AES-128
                        byte[] rsaPacket = new byte[curPacketSize];
                        System.Buffer.BlockCopy(Tcpbuff, TcpPacketStruct.POS_OF_MD5, rsaPacket, 0, curPacketSize);
                        byte[] decrypted = rsa.Decrypt(rsaPacket);
                        if (decrypted != null)
                        {
                            tokenLen = (decrypted.Length - TcpPacketStruct.SIZE_OF_MD5 - AESkeyLen) / 2;
                            //copy decryted byte to TcpBuff
                            curPacketSize = decrypted.Length;
                            System.Buffer.BlockCopy(decrypted, 0, Tcpbuff, TcpPacketStruct.POS_OF_MD5, curPacketSize);
                        }
                        if (tokenLen > 0 && CheckMD5NoDecrypt())
                        {
                            //get first salt (note, need & 0x7F to make sure value < 128)
                            salt = new byte[tokenLen];
                            System.Buffer.BlockCopy(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD, salt, 0, tokenLen);
                            for (int i = 0; i < tokenLen; i++) { salt[i] &= 0x7F; }

                            //get token, but de-convert with salt first
                            ConvertTextWithSalt(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD + tokenLen, tokenLen, SaltEnum.Sub);
                            token = Encoding.UTF8.GetString(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD + tokenLen, tokenLen);

                            //parse token to _id
                            try
                            {
                                _id = Convert.ToInt32(token);
                            }
                            catch (Exception ex)
                            {
                                _id = -1;
                                Log.Logger.Error("Exception ID of device is invalid: {0}", ex.Message);
                            }

                            //check token is exist
                            if (CheckTokenClient())
                            {
                                //get next 16B AES
                                AESkey = new byte[AESkeyLen];
                                System.Buffer.BlockCopy(Tcpbuff, TcpPacketStruct.POS_OF_PAYLOAD + tokenLen + tokenLen, AESkey, 0, AESkeyLen);

                                //send back ID to ACK with client
                                SendHandshakePackAsync(SendPackeTypeEnum.ACK);
                                IsHandshaked = true;

                                _log.LogInformation($"{token} TLS-handshake successfull!");

                                

                                OnTLSConnectedNotify();

                                ErrorRecv = false;
                            }
                            else
                            {
                                Log.Logger.Information($"{token} is not exits!");
                            }
                        }
                    }
                }
            } 
            else 
            {
                //if (IsHandshaked) ErrorRecv = false;
                if(curPacketSize == 1) //keep-alive packet
                {
                    if (!IsHandshaked)
                    {
                        //send rsa pub - key
                        SendHandshakePackAsync(SendPackeTypeEnum.Pubkey);
                    }
                    ErrorRecv = false;
                }
                
                //debug
                if(curPacketSize == 8 && IsHandshaked) 
                {
                    int conTime = BitConverter.ToInt32(Tcpbuff, 2);
                    int missTimeFrame = BitConverter.ToInt32(Tcpbuff, 6);
                    _log.LogInformation($"CurStt: {conTime} {missTimeFrame}");

                    ErrorRecv = false;
                }
            }
            
            if (ErrorRecv)
            {
                Disconnect();
            }
            else if(IsHandshaked)
            {
                ResetKeepAliveTimeoutTimer();
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
            _log.LogError($"TCP session caught an error with code {error}");
        }

        protected override void OnEmpty()
        {
            //Console.WriteLine($"ID {Id}, Pending byte: {BytesPending}, Sending bytes: {BytesSending}, Sent bytes: {BytesSent}");
        }


        internal bool SendTLSPacket(byte[] data, bool IsHaveEncrypt)
        {
            if(IsHandshaked)
            {
                if (IsHaveEncrypt)
                {
                    return true;
                }
                else
                    return SendPacketAsyncNoEncrypt(data);
            }
            return false;
        }

        //get length and add MD5 (then encrypt) to packet before send
        //void SendPacketAsync(byte[] data, int offset, int len)
        //length of payload make sure >= AES_BLOCK_LEN
        UInt32 packID = 0;
        bool SendPacketAsync(byte[] data)
        {
            //check data array have at least 1B at payload
            if (data != null && data.Length < (TcpPacketStruct.HEADER_LEN + AES.AES_BLOCK_LEN)) return false;
            int len = data.Length;

            byte[] md5Checksum;
            if (AESkey != null)
            {
                //encrypt data first then get md5 checksum
                AES.AES_Encrypt_Overwrite_Nopadding(data, TcpPacketStruct.POS_OF_PAYLOAD, len - TcpPacketStruct.HEADER_LEN, AESkey);

                md5Checksum = MD5.MD5Hash(data, TcpPacketStruct.POS_OF_PAYLOAD, len - TcpPacketStruct.HEADER_LEN);
                AES.AES_Encrypt_Overwrite_Nopadding(md5Checksum, 0, md5Checksum.Length, AESkey); //encrypt
            }
            else //this case has only one packet, that is sending pubkey
            {
                md5Checksum = MD5.MD5Hash(data, TcpPacketStruct.POS_OF_PAYLOAD, len - TcpPacketStruct.HEADER_LEN);
            }

            //copy md5 sum
            System.Buffer.BlockCopy(md5Checksum, 0, data, TcpPacketStruct.POS_OF_MD5, md5Checksum.Length);
            Console.Write("MD5sum: ");
            for(int j = 0; j < 16; j++)
            {
                Console.Write("{0} ", md5Checksum[j]);
            }
            Console.WriteLine("");

            Console.Write("payload: ");
            for (int j = TcpPacketStruct.POS_OF_PAYLOAD; j < (len - TcpPacketStruct.HEADER_LEN); j++)
            {
                Console.Write("{0} ", data[j]);
            }
            Console.WriteLine("");

            //copy len
            System.Buffer.BlockCopy(BitConverter.GetBytes((UInt16)(len - TcpPacketStruct.SIZE_OF_LEN)), 0, data, 0, TcpPacketStruct.SIZE_OF_LEN);

            //send packet string
            string sendString = "*" + Convert.ToHexString(BitConverter.GetBytes(packID)) + Convert.ToHexString(data) + "#"; //end with "#"

            packID++;

            return SendAsync(sendString);
        }

        //non-encrypt payload, but stil encrypt md5 (audio only), make sure len of payload >= 16
        int idPacket = 0;
        bool SendPacketAsyncNoEncrypt(byte[] data)
        {
            //check data array

            if (data != null && AESkey != null && data.Length >= (TcpPacketStruct.HEADER_LEN + AES.AES_BLOCK_LEN)) // 2B len, 16B md5
            {
                int len = data.Length;
                //byte[] md5Checksum = MD5.MD5Hash(data, TcpPacketStruct.POS_OF_PAYLOAD, len - TcpPacketStruct.HEADER_LEN);
                //AES.AES_Encrypt_Overwrite_Nopadding(md5Checksum, 0, TcpPacketStruct.SIZE_OF_MD5, AESkey); //encrypt

                ////copy md5 sum                                                                    
                //System.Buffer.BlockCopy(md5Checksum, 0, data, TcpPacketStruct.POS_OF_MD5, TcpPacketStruct.SIZE_OF_MD5);

                //copy len
                System.Buffer.BlockCopy(BitConverter.GetBytes((UInt16)(len - TcpPacketStruct.SIZE_OF_LEN)), 0, data, TcpPacketStruct.POS_OF_LEN, TcpPacketStruct.SIZE_OF_LEN);

                //send packet string
                string sendString = "*" + Convert.ToHexString(BitConverter.GetBytes(idPacket)) + Convert.ToHexString(data) + "#"; //end with "#"

                idPacket++;

                return SendAsync(sendString);
            }
            return false;
        }

        //1 2 3 4 5
        internal enum SendPackeTypeEnum { None, Pubkey, ACK };
        //send pubkey and ACK to handshake TLS
        void SendHandshakePackAsync(SendPackeTypeEnum type)
        {
            if (!IsHandshaked)
            {
                if (type == SendPackeTypeEnum.Pubkey)
                {
                    byte[] sendPubkeyBuff = new byte[rsa.publicKey.Modulus.Length + TcpPacketStruct.HEADER_LEN]; //4 byte length , 1 byte type, n byte pub key
                    
                    //copy pubkey
                    System.Buffer.BlockCopy(rsa.publicKey.Modulus, 0, sendPubkeyBuff, TcpPacketStruct.POS_OF_PAYLOAD, rsa.publicKey.Modulus.Length);

                    //trick exchange first and last two bytes to prevent hack
                    byte tmp = sendPubkeyBuff[TcpPacketStruct.POS_OF_PAYLOAD + 1];
                    sendPubkeyBuff[TcpPacketStruct.POS_OF_PAYLOAD + 1] = sendPubkeyBuff[TcpPacketStruct.POS_OF_PAYLOAD];
                    sendPubkeyBuff[TcpPacketStruct.POS_OF_PAYLOAD] = tmp;

                    tmp = sendPubkeyBuff[sendPubkeyBuff.Length - 2];
                    sendPubkeyBuff[sendPubkeyBuff.Length - 2] = sendPubkeyBuff[sendPubkeyBuff.Length - 1];
                    sendPubkeyBuff[sendPubkeyBuff.Length - 1] = tmp;

                    SendPacketAsync(sendPubkeyBuff);
                }
                else if (type == SendPackeTypeEnum.ACK)
                {
                    //send back salt
                    if(salt != null)
                    {
                        int lenTmp = salt.Length % AES.AES_BLOCK_LEN;
                        //make sure len of payload (TCP packet multiple of AES_BLOCK_LEN)
                        if(lenTmp != 0)
                        {
                            lenTmp = salt.Length + AES.AES_BLOCK_LEN - lenTmp;
                        }
                        else
                        {
                            lenTmp = salt.Length;
                        }

                        byte[] new_salt = new byte[lenTmp + TcpPacketStruct.HEADER_LEN];
                        System.Buffer.BlockCopy(salt, 0, new_salt, TcpPacketStruct.POS_OF_PAYLOAD, salt.Length);
                        for(int i = TcpPacketStruct.POS_OF_PAYLOAD; i < new_salt.Length; i++) { new_salt[i] |= 0x80; }
                        SendPacketAsync(new_salt);
                    }
                }
            }
        }

    }
}

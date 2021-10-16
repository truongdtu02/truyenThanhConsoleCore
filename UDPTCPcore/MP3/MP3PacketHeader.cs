using Security;
using System;
using System.Collections.Generic;

namespace MP3_ADU
{
    class MP3PacketHeader
    {
        const int AESkeyLen = 16;

        //static UINT16 len;
        static int len_offset = 0;

        //16B MD5
        static int md5_offset =                 2;

        //static byte type;
        static int type_offset =                2 + 16;
        public const int TYPE_POS =             2 + 16;

        //static UInt32 session;
        static int session_offset =             2 + 16 + 1;
        public const int SESSION_POS =          2 + 16 + 1;
        public const int SESSION_LEN =          4; //4B

        //16B AES_key-128
        static int aeskey_offset =              2 + 16 + 1 + 4;
        public const int AESKEY_POS =           2 + 16 + 1 + 4;

        //static byte volume;
        static int volume_offset =              2 + 16 + 1 + 4 + 16;

        //static long timestamp;
        static int timestamp_offset =           2 + 16 + 1 + 4 + 16 + 1;

        //static UInt32 frameID;
        static int frameID_offset =             2 + 16 + 1 + 4 + 16 + 1 + 8;

        //static byte numOfFrame; //1B
        static int numOfFrame_offset =          2 + 16 + 1 + 4 + 16 + 1 + 8 + 4;

        //static UInt16 sizeOfFirstFrame; //2B
        static int sizeOfFirstFrame_offset =    2 + 16 + 1 + 4 + 16 + 1 + 8 + 4 + 1;

        //static UInt16 frameSize; //2B
        static int frameSize_offset =           2 + 16 + 1 + 4 + 16 + 1 + 8 + 4 + 1 + 2;

        //static byte timePerFrame; //1B (ms)
        static int timePerFrame_offset =        2 + 16 + 1 + 4 + 16 + 1 + 8 + 4 + 1 + 2 + 2;

        public const int HEADER_SIZE =          2 + 16 + 1 + 4 + 16 + 1 + 8 + 4 + 1 + 2 + 2 + 1;

        public static byte[] Packet(List<byte[]> mp3FrameList, byte _volume, long _timestamp, UInt32 _frameId,
            UInt16 _frameSize, byte _timePerFrame, int _totalLen)
        {
            if (mp3FrameList.Count > 255) return null;

            byte[] buff = new byte[_totalLen + HEADER_SIZE];

            //debug
            //byte[] buff = new byte[1020]; //fixed size of packet 6400B is max case 43 packet, 1020-5


            //create AES_key
            Random rd = new Random();
            byte[] AESkey = new byte[AESkeyLen];
            rd.NextBytes(AESkey);
            //copy aes key
            System.Buffer.BlockCopy(AESkey, 0, buff, aeskey_offset, AESkeyLen);

            //copy volume
            buff[volume_offset] = _volume;

            //copy timestamp
            System.Buffer.BlockCopy(BitConverter.GetBytes(_timestamp), 0, buff, timestamp_offset, sizeof(long));

            //copy frame id
            System.Buffer.BlockCopy(BitConverter.GetBytes(_frameId), 0, buff, frameID_offset, sizeof(UInt32));

            //copy num of frame
            buff[numOfFrame_offset] = (byte)mp3FrameList.Count;

            //copy size of first frame
            System.Buffer.BlockCopy(BitConverter.GetBytes((UInt16)mp3FrameList[0].Length), 0, buff, sizeOfFirstFrame_offset, sizeof(UInt16));

            //copy frame size
            System.Buffer.BlockCopy(BitConverter.GetBytes(_frameSize), 0, buff, frameSize_offset, sizeof(UInt16));

            //copy time per frame
            buff[timePerFrame_offset] = _timePerFrame;

            int offsetBuff = HEADER_SIZE;
            //copy frame
            foreach(var fr in mp3FrameList)
            {
                System.Buffer.BlockCopy(fr, 0, buff, offsetBuff, fr.Length);
                offsetBuff += fr.Length;
            }

            //debug add a,b,c,d,e
            //for (int i = 1; i < 17; i++)
            //{
            //    buff[buff.Length - i] = (byte)('z' - i);
            //}

            //encrypt with above key, from volume
            AES.AES_Encrypt_Overwrite_Nopadding(buff, volume_offset, buff.Length - volume_offset, AESkey);

            return buff;
        }
    }
}

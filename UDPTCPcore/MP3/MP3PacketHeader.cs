using Security;
using System;
using System.Collections.Generic;

namespace MP3_ADU
{
    class MP3PacketHeader
    {
        const int AESkeyLen = 16;

        //static byte type;
        static int type_offset = 0;

        //static UInt32 session;
        static int session_offset =             1;

        //11B padding
        static int padding_offset =             1 + 4;

        //16B AES_key-128
        static int aeskey_offset =              1 + 4 + 11;

        //static byte volume;
        static int volume_offset =              1 + 4 + 11 + 16;

        //static long timestamp;
        static int timestamp_offset =           1 + 4 + 11 + 16 + 1;

        //static UInt32 frameID;
        static int frameID_offset =             1 + 4 + 11 + 16 + 1 + 8;

        //static byte numOfFrame; //1B
        static int numOfFrame_offset =          1 + 4 + 11 + 16 + 1 + 8 + 4;

        //static UInt16 sizeOfFirstFrame; //2B
        static int sizeOfFirstFrame_offset =    1 + 4 + 11 + 16 + 1 + 8 + 4 + 1;

        //static UInt16 frameSize; //2B
        static int frameSize_offset =           1 + 4 + 11 + 16 + 1 + 8 + 4 + 1 + 2;

        //static byte timePerFrame; //1B (ms)
        static int timePerFrame_offset =        1 + 4 + 11 + 16 + 1 + 8 + 4 + 1 + 2 + 2;

        const int HEADER_SIZE =                 1 + 4 + 11 + 16 + 1 + 8 + 4 + 1 + 2 + 2 + 1;

        internal const int HEADER_NOENCRYPT_SIZE = 1 + 4 + 11 + 16;

        public static byte[] Packet(List<byte[]> mp3FrameList, byte _volume, long _timestamp, UInt32 _frameId,
            UInt16 _frameSize, byte _timePerFrame, int _totalLen)
        {
            if (mp3FrameList.Count > 255) return null;
            
            byte[] buff = new byte[_totalLen + HEADER_SIZE];

            //random padding
            Random rd = new Random();
            for (int i = padding_offset; i < aeskey_offset; i++) buff[i] = (byte)rd.Next();

            //create AES_key
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

            //encrypt with above key, from volume
            AES.AES_Encrypt_Overwrite_Nopadding(buff, volume_offset, buff.Length - volume_offset, AESkey);

            return buff;
        }
    }
}

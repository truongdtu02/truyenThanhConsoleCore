using System;
using System.Collections.Generic;

namespace UDPTCPcore
{
    class MP3PacketHeader
    {
        static int session;
        static int session_offset = 0;

        static long timestamp;
        static int timestamp_offset = 4;

        static UInt32 frameID;
        static int frameID_offset = 4 + 8;

        static UInt16 numOfFrame; //2B
        static int numOfFrame_offset = 4 + 8 + 4;

        static UInt16 sizeOfFirstFrame; //2B
        static int sizeOfFirstFrame_offset = 4 + 8 + 4 + 2;

        static UInt16 frameSize; //2B
        static int frameSize_offset = 4 + 8 + 4 + 2 + 2;

        const int HEADER_SIZE = 4 + 8 + 4 + 2 + 2 + 2;

        public static byte[] Packet(List<byte[]> mp3FrameList, int totalLen, int _frameSize, UInt32 _frameId, long _timestamp)
        {
            numOfFrame = (UInt16)mp3FrameList.Count;
            sizeOfFirstFrame = (UInt16)mp3FrameList[0].Length;
            frameSize = (UInt16)_frameSize;
            frameID = _frameId;
            timestamp = _timestamp;

            byte[] buff = new byte[totalLen + HEADER_SIZE];

            //copy header
            System.Buffer.BlockCopy(BitConverter.GetBytes(timestamp), 0, buff, timestamp_offset, sizeof(long));
            System.Buffer.BlockCopy(BitConverter.GetBytes(frameID), 0, buff, frameID_offset, sizeof(UInt32));
            System.Buffer.BlockCopy(BitConverter.GetBytes(numOfFrame), 0, buff, numOfFrame_offset, sizeof(UInt16));
            System.Buffer.BlockCopy(BitConverter.GetBytes(sizeOfFirstFrame), 0, buff, sizeOfFirstFrame_offset, sizeof(UInt16));
            System.Buffer.BlockCopy(BitConverter.GetBytes(frameSize), 0, buff, frameSize_offset, sizeof(UInt16));

            int offsetBuff = HEADER_SIZE;
            //copy frame
            foreach(var fr in mp3FrameList)
            {
                System.Buffer.BlockCopy(fr, 0, buff, offsetBuff, fr.Length);
                offsetBuff += fr.Length;
            }

            return buff;
        }
    }
}

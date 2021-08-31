using System.IO;

namespace MP3_ADU
{
    class MP3_Frame_CBR
    {
        //constructor initialize
        FileStream mp3_buff;
        int mp3_buff_len = 0;
        bool bIsValidMP3 = false;

        //mp3 is MPEG 1 Layer III or MPEG 2 layer III, detail: https://en.wikipedia.org/wiki/MP3

        //mp3 header include 32-bit
        // byte0    byte1   byte2   byte3
        //bit 31                        0
        //detail: http://www.mp3-tech.org/programmer/frame_header

        //bit 31-21 is frame sync, all bit is 1, (byte0 == FF) && (byte1 & 0xE0 == 0xE0)

        //bit 20-19 : MPEG version, 11: V1, 10: V2
        int version, version_first_header;

        public int Version { get => version; }

        //bit 18-17 Layer, just consider layer III, 01
        const int layer = 3;
        public static int Layer => layer;

        //bit 16, protected bit, don't count

        //bit 15-12 bitrate
        static readonly int[] bitrate_V1_L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 }; // MPEG 1, layer III
        static readonly int[] bitrate_V2_L3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 }; // MPEG 2, layer III
        int bitrate, bitrate_first_header;
        public int Bitrate { get => bitrate; }

        //bit 11-10 sample rate
        static readonly int[] sample_rate_V1 = { 44100, 48000, 32000, 0 };
        static readonly int[] sample_rate_V2 = { 22050, 24000, 16000, 0 };
        int sample_rate, sample_rate_first_header;
        public int Sample_rate { get => sample_rate; }

        //sample per frame
        int sample_per_frame;
        static readonly int[] sample_per_frame_version = { 1152, 576 };

        public int Sample_per_frame { get => sample_per_frame; }

        //bit 9, padding bit
        int padding;
        public int Padding { get => padding; }

        int sideInfoSize;
        public int SideInfoSize { get => sideInfoSize; }


        //channel bit 7, 6; 00, 01, 10, 11 : stereo, joint stereo, dual channel, mono
        int channel;
        public int Channel { get => channel; }

        //v2 8-bit after header, v1 9-bit after header
        //int main_data_begin;
        //public int MainDataBegin { get => main_data_begin; }

        int totalFrame = 0;
        public int TotalFrame { get => totalFrame; }

        int frame_size = 0;
        public int Frame_size { get => frame_size; }

        double timePerFrame_ms;
        public double TimePerFrame_ms { get => timePerFrame_ms; }

        int startPoint, endPoint;

        bool IsFirst = true;

        //read from specific position in FileStream
        bool MP3ReadOffset(int mp3Offset, byte[] arr, int arrOffset, int count)
        {
            try
            {
                mp3_buff.Seek(mp3Offset, SeekOrigin.Begin);
                mp3_buff.Read(arr, arrOffset, count);
            }
            catch
            {
                return false;
            }
            return true;
        }


        //get size file to analyze header + size info
        bool IsValidHeader(int offset)
        {
            if (offset + 6 > mp3_buff_len) return false; //out of file
            byte[] buff = new byte[6];
            if (!MP3ReadOffset(offset, buff, 0, 6)) return false;

            //get infor header
            int header = (int)buff[3] | ((int)buff[2] << 8) | ((int)buff[1] << 16) | ((int)buff[0] << 24);

            //get version
            int tmp = (header >> 19) & 0b11;
            if (tmp == 0b11)
                version = 1;
            else if (tmp == 0b10)
                version = 2;
            else
                return false;

            //get layer
            tmp = (header >> 17) & 0b11;
            if (tmp != 0b01) //layer III
                return false;

            //get bitrate
            tmp = (header >> 12) & 0b1111;
            if ((tmp == 0) || (tmp == 0b1111))
                return false;
            if (version == 1)
                bitrate = bitrate_V1_L3[tmp];
            else if (version == 2)
                bitrate = bitrate_V2_L3[tmp];

            //get smaple rate
            tmp = (header >> 10) & 0b11;
            if (tmp == 0b11)
                return false;
            if (version == 1)
                sample_rate = sample_rate_V1[tmp];
            else if (version == 2)
                sample_rate = sample_rate_V2[tmp];

            //check, if it is next frame, compare with first frame
            if(!IsFirst) //next frame (CBR)
            {
                if ((version_first_header != version) || (sample_rate_first_header != sample_rate) || (bitrate_first_header != bitrate))
                    return false;
            }

            //just get one time
            if(IsFirst) 
            {
                //get padding
                padding = (header >> 9) & 1;

                //get channel
                channel = (header >> 6) & 3; //0b11

                //get side info size
                if (version == 1 && channel != 3) // v1, stereo
                {
                    sideInfoSize = 32;
                }
                else if (version == 2 && channel == 3) //v2, mono
                {
                    sideInfoSize = 9;
                }
                else
                {
                    sideInfoSize = 17;
                }
                //get main data begin
                //if (version == 1)
                //{
                //    main_data_begin = ((int)buff[4] << 1) | (((int)buff[5] >> 7) & 1); // 9-bit
                //}
                //else
                //{
                //    main_data_begin = (int)buff[4]; // 8-bit
                //}

                //get sample per frame
                sample_per_frame = sample_per_frame_version[version - 1];

                //timePerFrame_ms
                timePerFrame_ms = 1000.0 * (double)sample_per_frame / (double)sample_rate;

                //get frame size
                double frame_size_tmp = bitrate * 1000 * sample_per_frame / 8 / sample_rate + padding;
                frame_size = (int)frame_size_tmp;
            }

            //check next frame
            if ((offset + frame_size) > mp3_buff_len) //out of range
            {
                return false;
            }
            return true;
        }

        //mp3 is valid when at least a half of file is mp3 frames
        //public bool CheckValidMP3_V1()
        //{
        //    int index_buff_mp3 = 0;
        //    totalFrame = 0;
        //    byte[] buff = new byte[2];

        //    while ((index_buff_mp3 + 6) > mp3_buff_len) // a frame has at least 6 bytes (4B header + 2B(in sideInfo))
        //    {
        //        mp3_buff.Read(buff, index_buff_mp3, 2);
        //        if ((buff[0] == 0xFF) && ((buff[1] & 0xE0) == 0xE0)) //sync bit
        //        {
        //            if (IsValidHeader(index_buff_mp3))
        //            {
        //                index_buff_mp3 += frame_size;
        //                totalFrame++;
        //                continue;
        //            }
        //        }
        //        index_buff_mp3++;
        //    }
        //    if ((totalFrame * frame_size) > (mp3_buff_len / 2)) return false;
        //    return true;
        //}

        public bool CheckValidMP3(int standardVersion, int standardBitrate, int standardSamplerate)
        {
            int index_buff_mp3 = 0;
            totalFrame = 0;
            byte[] buff = new byte[2];

            //get first valid header
            while ((index_buff_mp3 + 6) < mp3_buff_len) // a frame has at least 6 bytes (4B header + 2B(in sideInfo))
            {
                //mp3_buff.Read(buff, index_buff_mp3, 2);
                if (!MP3ReadOffset(index_buff_mp3, buff, 0, 2)) return false;
                if ((buff[0] == 0xFF) && ((buff[1] & 0xE0) == 0xE0)) //sync bit
                {
                    if (IsValidHeader(index_buff_mp3))
                    {
                        if(version == standardVersion && bitrate == standardBitrate && sample_rate == standardSamplerate)
                        {
                            totalFrame = 1;
                            IsFirst = false;
                            oldPos = index_buff_mp3; //set start of first frame for func ReadNextFrame
                            startPoint = index_buff_mp3;
                            index_buff_mp3 += frame_size;
                            version_first_header = standardVersion;
                            bitrate_first_header = standardBitrate;
                            sample_rate_first_header = standardSamplerate;
                            break;
                        }
                        index_buff_mp3 += frame_size;
                        continue;
                    }
                }
                index_buff_mp3++;
            }

            //get next frames
            while(index_buff_mp3 < mp3_buff_len)
            {
                if(IsValidHeader(index_buff_mp3))
                {
                    totalFrame++;
                    index_buff_mp3 += frame_size;
                    continue;
                }
                break;
            }
            endPoint = index_buff_mp3;
            if ((totalFrame * frame_size) < (mp3_buff_len / 2)) 
                return false;
            return true;
        }

        int oldPos = 0; //for read next frame
        public byte[] ReadNextFrame()
        {
            if ((oldPos + frame_size) > endPoint) return null;
            byte[] buff = new byte[frame_size];
            //mp3_buff.Read(buff, oldPos, frame_size);
            if (!MP3ReadOffset(oldPos, buff, 0, frame_size)) return null;
            oldPos += frame_size;
            return buff;
        }

        int GetMainDataBeginLen(int offset)
        {
            byte[] buff = new byte[2];
            //mp3_buff.Read(buff, offset + 4, 2);
            if (!MP3ReadOffset(offset + 4, buff, 0, 2)) return -1;
            if (version == 1)
            {
                return ((int)buff[0] << 1) | (((int)buff[1] >> 7) & 1); // 9-bit
            }
            else
            {
                return (int)buff[0]; // 8-bit
            }
        }

        //read next frame + main_data_begin (place between side_info and data_main)
        //main_data_begin:132. oldPos = 288 (got 2 frames)
        public byte[] ReadNextADU()
        {
            if ((oldPos + frame_size) > endPoint) return null;

            int main_data_begin = GetMainDataBeginLen(oldPos); //132
            if (main_data_begin < 0) return null;

            byte[] ADUframe = new byte[frame_size + main_data_begin]; //144+132
            
            int main_data_size = frame_size - sideInfoSize - 4; //131
            int remainder = main_data_begin % main_data_size; //1
            int numOfFrameRelated; //contain main_data_begin of frame which function will return

            //note: frame structure (4B header, nB side-info, mB side-info) (4+n+m = frame_size)
            //real start point of main_data_begin (MDB) in mp3_buff
            int startPointMDB;

            if (remainder != 0)
            {
                numOfFrameRelated = main_data_begin / main_data_size + 1; //2
                startPointMDB = oldPos - (numOfFrameRelated - 1) * frame_size - remainder; //143
            }
            else
            {
                numOfFrameRelated = main_data_begin / main_data_size;
                startPointMDB = oldPos - (numOfFrameRelated - 1) * frame_size - main_data_size;
            }
            //check mp3_buff have enough related frames
            if ((oldPos - numOfFrameRelated * frame_size) < startPoint) return null;

            //copy first header + side_info
            //mp3_buff.Read(ADUframe, oldPos, 4 + sideInfoSize);
            if (!MP3ReadOffset(oldPos, ADUframe, 0, 4 + sideInfoSize)) return null;

            //get ADU data
            int ADUframeOffset = 4 + sideInfoSize; //13
            while (startPointMDB < oldPos)
            {
                if(remainder != 0)
                {
                    if (!MP3ReadOffset(startPointMDB, ADUframe, ADUframeOffset, remainder)) return null;
                    remainder = 0;
                    startPointMDB += remainder + 4 + sideInfoSize; //144 + 13
                    ADUframeOffset += remainder; //13 + 1
                }
                else
                {
                    if (!MP3ReadOffset(startPointMDB, ADUframe, ADUframeOffset, main_data_size)) return null;
                    startPointMDB += frame_size; // 144 + 144 + 13
                    ADUframeOffset += main_data_size; // 13 + 1 + 131
                }
            }

            //get remain main_data
            if (!MP3ReadOffset(startPointMDB, ADUframe, ADUframeOffset, main_data_size)) return null;

            return ADUframe;
        }

        public byte[] ReadFrameIndex(int frameIndex)
        {
            int pos = frameIndex * frame_size;
            if ((pos + frame_size) > endPoint || frameIndex < 0) return null;

            byte[] buff = new byte[frame_size];
            if (!MP3ReadOffset(pos, buff, 0, frame_size)) return null;
            return buff;
        }

        //constructor
        public MP3_Frame_CBR(FileStream mp3Stream)
        {
            mp3_buff = mp3Stream;
            mp3_buff_len = (int)mp3_buff.Length;
        }
    }

}

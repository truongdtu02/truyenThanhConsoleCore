using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UDPTCPcore
{
    public class TruyenthanhDatabaseSettings : ITruyenthanhDatabaseSettings
    {
        public string AccountCollectionName { get; set; }
        public string DeviceCollectionName { get; set; }
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; }
        //public string PathSong { get; set; }
        //public string PassAdmin { get; set; }
        //public int PortUDPBroadcast { get; set; }
        //public int PortFFmpeg { get; set; }
        //public int IntervalCheckRequestUDP { get; set; }

    }

    public interface ITruyenthanhDatabaseSettings
    {
        string AccountCollectionName { get; set; }
        string DeviceCollectionName { get; set; }
        string ConnectionString { get; set; }
        string DatabaseName { get; set; }
        //string PathSong { get; set; }
        //string PassAdmin { get; set; }
        //int PortUDPBroadcast { get; set; }
        //int PortFFmpeg { get; set; }
        //int IntervalCheckRequestUDP { get; set; }
    }
}

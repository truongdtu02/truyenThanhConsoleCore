using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.IO;
using Security;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Timers;
using System.Threading;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace UDPTCPcore
{
    

    class Program
    {
        internal static IHost host { get; private set; }
        static long timeStart;
        static string api_config_file;
        internal static UInt32 listID_host_api_cycle;
        internal static string listID_host_api;
        internal static string dvStt_mp3_api, ctrPlay_mp3_api, sttPlay_mp3_api, url_mp3_api;
        internal static Thread api_request_thread, server_tcp_thread, api_server_thread;
        internal static DeviceServer deviceServer;
        internal static string mp3_dir;
        internal static int frames_per_packet, time_per_frame, send_buffer_size;

        static void Main(string[] args)
        {           
            StartUp();

            //get setting in xml file
            var doc = new XmlDocument();
            doc.Load(api_config_file);
            //get  api_mp3 first
            XmlNodeList node = doc.SelectNodes("//api_list/api_mp3");

            foreach (XmlNode nd in node)
            {
                url_mp3_api = nd["url"].InnerText;
                dvStt_mp3_api = nd["dv_stt"].InnerText;
                ctrPlay_mp3_api = nd["ctr_play"].InnerText;
                sttPlay_mp3_api = nd["stt_play"].InnerText;
            }

            //get  api_host
            node = doc.SelectNodes("//api_list/api_host");
            string url_tmp;
            foreach (XmlNode nd in node)
            {
                url_tmp = nd["url"].InnerText;
                listID_host_api = url_tmp + nd["list_id"].InnerText;
                listID_host_api_cycle = Convert.ToUInt32(nd["list_id_request_cycle"].InnerText);
            }

            NTPServer ntpServer;
            if (OperatingSystem.IsWindows())
            {
                //can't run in local computer
                //NTPServer ntpServer = host.Services.GetRequiredService<NTPServer>();
                //ntpServer.Start();
            }
            else
            {
                ntpServer = host.Services.GetRequiredService<NTPServer>();
                ntpServer.Start();
            }

            deviceServer = host.Services.GetRequiredService<DeviceServer>();
            server_tcp_thread = new Thread(() => { deviceServer.Run(); });
            //deviceServer.Run();
            server_tcp_thread.Priority = ThreadPriority.Highest;
            server_tcp_thread.Start();

            //api_request_thread
            api_request_thread = new Thread(async () =>
            {
                await MyHttpClient.api_request_task();
            });
            api_request_thread.Priority = ThreadPriority.Lowest;
            api_request_thread.Start();

            //api_server_thread handle request from user
            api_server_thread = new Thread(async () =>
            {
                await MyHttpServer.httpServerHandle();
            });
            api_server_thread.Priority = ThreadPriority.Lowest;
            api_server_thread.Start();

            //deviceServer.Run();

            while (true) { }
        }

        static void BuildConfig(IConfigurationBuilder builder)
        {
            if (OperatingSystem.IsWindows())
            {
                builder.SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettingsWin.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENRT") ?? "Production"}.json", optional: true)
                    .AddEnvironmentVariables();
            }
            else
            {
                builder.SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettingsLinux.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENRT") ?? "Production"}.json", optional: true)
                    .AddEnvironmentVariables();
            }
        }
        static void StartUp()
        {
            //Console.WriteLine("Hello World!");
            var builder = new ConfigurationBuilder();
            BuildConfig(builder);
            var configurationroot = builder.Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Build())
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("log.txt")
                .CreateLogger();

            Log.Logger.Information("Application Starting");

            api_config_file = configurationroot.GetValue<string>("ConfigAPIXml");
            mp3_dir = configurationroot.GetValue<string>("Mp3Directory");
            frames_per_packet = configurationroot.GetValue<int>("FramesPerPacket"); 
            time_per_frame = configurationroot.GetValue<int>("TimePerFrame");
            send_buffer_size = configurationroot.GetValue<int>("SendBufferSize"); 

             host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    //register option to DI
                    //services.Configure<TruyenthanhDatabaseSettings>(
                    //    Configuration.GetSection(nameof(TruyenthanhDatabaseSettings))); 
                    services.Configure<TruyenthanhDatabaseSettings>(
                          configurationroot.GetSection(nameof(TruyenthanhDatabaseSettings)));

                    //register DI by factory
                    services.AddSingleton<ITruyenthanhDatabaseSettings>(sp =>
                        sp.GetRequiredService<IOptions<TruyenthanhDatabaseSettings>>().Value);
                    //sp.GetRequiredService<IOptions<TruyenthanhDatabaseSettings>>().Value:
                    // get instance of object option is registered above
                    services.AddSingleton<DeviceServer>(sp => {
                        DeviceServer deviceServer = new DeviceServer(IPAddress.Any, configurationroot.GetSection("DeviceServer").GetValue<int>("DevicePort"),
                            sp.GetRequiredService<ILogger<DeviceServer>>());
                        return deviceServer;
                    });
                    services.AddTransient<DeviceSession>(sp =>
                    {
                        DeviceSession deviceSession = new DeviceSession(
                            sp.GetRequiredService<DeviceServer>(),
                            sp.GetRequiredService<ILogger<DeviceSession>>());
                        return deviceSession;
                    });

                    services.AddTransient<TLSSession>(sp =>
                    {
                        TLSSession tlsSession = new TLSSession(
                            sp.GetRequiredService<DeviceServer>(),
                            sp.GetRequiredService<ILogger<TLSSession>>());
                        return tlsSession;
                    });

                    services.AddSingleton<NTPServer>(sp =>
                    {
                        NTPServer ntpServer = new NTPServer(IPAddress.Any, configurationroot.GetSection("DeviceServer").GetValue<int>("DevicePort"),
                            sp.GetRequiredService<ILogger<NTPServer>>());
                        return ntpServer;
                    });

                })
                .UseSerilog()
                .Build();
        }
    }
}
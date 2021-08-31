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

namespace UDPTCPcore
{
    class Program
    {
        internal static IHost host { get; private set; }
        static long timeStart;

        static void Main(string[] args)
        {

            MD5.MD5Hash("hello, anh dep zai!!!");
            //RSA rsa = new RSA(); rsa.Run();
            StartUp();
            //var svc = ActivatorUtilities.CreateInstance<IGreetingService>(host.Services);
            //var svc = host.Services.GetRequiredService<IGreetingService>();
            //svc.Run();
            //AES.Run();
            //RSA rsa = new RSA(); rsa.Run();
            //byte[] test = new byte[0]; //still correct
            DeviceServer deviceServer = host.Services.GetRequiredService<DeviceServer>();
            deviceServer.Run();
        }

        static void BuildConfig(IConfigurationBuilder builder)
        {
            builder.SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENRT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables();
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
                    services.AddTransient<DeviceSession>( sp =>
                    {
                        DeviceSession deviceSession = new DeviceSession(
                            sp.GetRequiredService<DeviceServer>(),
                            sp.GetRequiredService<ILogger<DeviceSession>>());
                        return deviceSession;
                    });

                })
                .UseSerilog()
                .Build();
        }
    }
}

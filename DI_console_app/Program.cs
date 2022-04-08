using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

//DI, Serilog and settings

namespace DI_console_app
{
    class Program
    {
        static void repeatTaskUseStopWatch()
        {
            //launch timer
            Stopwatch stopWatchSend = new Stopwatch();
            stopWatchSend.Start();

            long count = 1;
            long interval = 600, remainTime = 0;

            while(true)
            {
                double i = 0;
                for(double j = 0; j < 1000; j++)
                {
                    i += (j + 2 * j + j * j);
                    Random random = new Random();
                    i += (double)random.Next();
                }
                remainTime = count * interval - (long)stopWatchSend.Elapsed.TotalMilliseconds;
                count++;
                Console.WriteLine($"Remain time {remainTime}. Res {i}. Count {count}");
                if(remainTime > 0)
                {
                    Thread.Sleep((int)remainTime);
                }
            }
        }
        static void Main(string[] args)
        {
            repeatTaskUseStopWatch();

            //Console.WriteLine("Hello World!");
            //var builder = new ConfigurationBuilder();
            //BuildConfig(builder);

            //Log.Logger = new LoggerConfiguration()
            //    .ReadFrom.Configuration(builder.Build())
            //    .Enrich.FromLogContext()
            //    .WriteTo.Console()
            //    .CreateLogger();

            //Log.Logger.Information("Application Starting");

            //var host = Host.CreateDefaultBuilder()
            //    .ConfigureServices((context, services) =>
            //    {
            //        services.AddTransient<IGreetingService ,GreetingService>(); //give me an instance every time I ask for
            //    })
            //    .UseSerilog()
            //    .Build();

            ////var svc = ActivatorUtilities.CreateInstance<IGreetingService>(host.Services);
            //var svc = host.Services.GetRequiredService<IGreetingService>();
            //svc.Run();




        }

        static void BuildConfig(IConfigurationBuilder builder)
        {
            builder.SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENRT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables();
        }

    }
}

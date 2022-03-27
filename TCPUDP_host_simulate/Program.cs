using System;
using System.IO;
using System.Net;
using System.Timers;
using System.Threading;
using System.Text;

namespace TCPUDP_host
{
    class Program
    {
        static bool IsServer = false; //== true when want run httplistener
        static async Task<int> server_handle(string[] args)
        {
            var server = new MyHttpServer(new string[] { "http://127.0.0.1:51000/" }); //local access
            //var server = new MyHttpServer(new string[] { "http://*:5000/" }); //uinversal access (any ip)
            await server.StartAsync();
            while (true) { }
            return 0;
        } 
        static async Task<int> client_handle(string[] args)
        {
            

            return 0;
        }
        static async Task Main(string[] args)
        {
            Console.WriteLine("Welcom to host simulate program");
            //int cnt = 0;
            if(args.Length < 1)
            {
                goto err_param;
            }

            if(args[0] == "server")
            {
                IsServer = true;
                if(await server_handle(args) != 0)
                    goto err_param;
            }
            else if(args[0] == "client")
            {
                IsServer = false;
                if (await client_handle(args) != 0)
                    goto err_param;
            }
            else
            {
                goto err_param;
            }


            while (true) { }

            err_param:
            Console.WriteLine("Wrong param");
        }

    }
}

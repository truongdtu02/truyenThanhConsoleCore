using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.IO;
using Security;
using Microsoft.Extensions.Logging;
using System.Timers;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace UDPTCPcore
{
    //public class listIDJson
    //{
    //    [JsonProperty("ids")]
    //    public List<string> listID { get; set; }
    //}

    class MyHttpClient
    {
        internal static async Task api_request_task()
        {
            var client = new HttpClient();
            UInt32 loop_time = 0;
            bool listIDFirstRequest = true;
            while (true)
            {
                loop_time++;
                if (loop_time % Program.listID_host_api_cycle == 0 || listIDFirstRequest)
                {
                    // Call asynchronous network methods in a try/catch block to handle exceptions.
                    try
                    {
                        HttpResponseMessage response = await client.GetAsync(Program.listID_host_api);
                        response.EnsureSuccessStatusCode();
                        //return;
                        string responseBody = await response.Content.ReadAsStringAsync();
                        // Above three lines can be replaced with new helper method below
                        // string responseBody = await client.GetStringAsync(uri);

                        //Console.WriteLine(responseBody);
                        //var listParse = JsonConvert.DeserializeObject<listIDJson>(responseBody);
                        //Program.deviceServer.updateListID(listParse.listID);
                        try
                        {
                            dynamic myJObject = JsonConvert.DeserializeObject(responseBody);
                            var listParse = myJObject.ids.ToObject<List<Int32>>();
                            Program.deviceServer.updateListID(listParse);
                            Log.Logger.Information("Request listID successfully");
                            listIDFirstRequest = false;
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error("Exception parse list_id reponse: {0}", ex.Message);
                        }
                    }
                    catch (HttpRequestException e)
                    {
                        Log.Logger.Error("Exception http list_id:  {0}", e.Message);
                    }
                }
                Thread.Sleep(1000);
            }
        }
    }
    // Chạy một HTTP Server, prefixes example: new string[] { "http://*:8080/" }
    class MyHttpServer
    {
        private HttpListener listener;

        public MyHttpServer(params string[] prefixes)
        {
            if (!HttpListener.IsSupported)
                throw new Exception("Máy không hỗ trợ HttpListener.");

            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");

            // Khởi tạo HttpListener
            listener = new HttpListener();
            foreach (string prefix in prefixes)
                listener.Prefixes.Add(prefix);

        }

        internal static async Task httpServerHandle()
        {
            var httpServer = new MyHttpServer(new string[] { Program.url_mp3_api }); //local access
            //var server = new MyHttpServer(new string[] { "http://*:5000/" }); //uinversal access (any ip)
            try
            {
                await httpServer.StartAsync();
            }
            catch (Exception e)
            {
                Log.Logger.Error("Exception HttpServer start: {0}", e.Message);
            }
            while (true) { }
        }

        internal async Task StartAsync()
        {
            // Bắt đầu lắng nghe kết nối HTTP
            listener.Start();
            do
            {
                try
                {
                    Log.Logger.Information($"{DateTime.Now.ToLongTimeString()} : waiting a client connect");

                    // Một client kết nối đến
                    HttpListenerContext context = await listener.GetContextAsync();
                    await ProcessRequest(context);

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                Log.Logger.Information("...");

            }
            while (listener.IsListening);
            Log.Logger.Error("HttpServer handle request from user is closed!");
        }

        // Xử lý trả về nội dung tùy thuộc vào URL truy cập
        //      /               hiện thị dòng Hello World
        //      /stop           dừng máy chủ
        //      /json           trả về một nội dung json
        //      /anh2.png       trả về một file ảnh 
        //      /requestinfo    thông tin truy vấn
        async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            HttpListenerResponse response = context.Response;
            if (request == null || request.Url == null)
                return;
                //throw new Exception("request url is null");

            Log.Logger.Information($"{request.HttpMethod} {request.RawUrl} {request.Url.AbsolutePath}");

            // Lấy stream / gửi dữ liệu về cho client
            var outputstream = response.OutputStream;

            switch (request.Url.AbsolutePath)
            {
                case "/control_play":
                    {
                        //string responseBody = await response.Content.ReadAsStringAsync();
                        //read and parse control param (from body of request)
                        string requestText;
                        try
                        {
                            using (var reader = new StreamReader(request.InputStream,
                                     request.ContentEncoding))
                            {
                                requestText = await reader.ReadToEndAsync();
                            }

                            dynamic myJObject = JsonConvert.DeserializeObject(requestText);
                            //var listParse = myJObject.ids.ToObject<List<int>>();
                            //string action = myJObject.action.ToString().ToLower();
                            //string session = myJObject.session.ToString();
                            //string state = myJObject.state.ToString().ToLower();
                            Log.Logger.Information("Request control_play_api valid.");
                            string stateStr = myJObject.state.ToString().ToLower();
                            var sessionTmp = new SessionPlay()
                            {
                                action = myJObject.action.ToString().ToLower(),
                                listID = myJObject.ids.ToObject<List<Int32>>(),
                                session = myJObject.session.ToString(),
                                //state = myJObject.state.ToString().ToLower(),
                                type = SessionPlay.TypeSession.mp3
                            };
                            if(stateStr == "play")
                            {
                                sessionTmp.state = SessionPlay.StateSession.play;
                            } else if(stateStr == "pause")
                            {
                                sessionTmp.state = SessionPlay.StateSession.pause;
                            }
                            else if(stateStr == "stop")
                            {
                                sessionTmp.state = SessionPlay.StateSession.stop;
                            }
                            else
                            {
                                sessionTmp.state = SessionPlay.StateSession.none;
                            }
                            //string responseString;
                            //if (Program.deviceServer.updateListSesionPlay(sessionTmp) == 0)
                            //{
                            //    responseString = "{Ok}";
                            //}
                            //else
                            //{
                            //    responseString = "{Fail}";
                            //}
                            string responseString = Program.deviceServer.updateListSesionPlay(sessionTmp);

                            // Gửi thông tin về cho Client
                            context.Response.Headers.Add("content-type", "application/json");
                            context.Response.StatusCode = (int)HttpStatusCode.OK;

                            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                            response.ContentLength64 = buffer.Length;
                            await outputstream.WriteAsync(buffer, 0, buffer.Length);
                            Log.Logger.Information("Reponse to control_play_api successfull.");
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error("Exception control_play request: {0}.", ex.Message);
                        }
                    }
                    break;
                case "/":
                    {
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Hello world!");
                        response.ContentLength64 = buffer.Length;
                        await outputstream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    break;

                case "/stop":
                    {
                        listener.Stop();
                        Console.WriteLine("stop http");
                    }
                    break;

                case "/json":
                    {
                        response.Headers.Add("Content-Type", "application/json");
                        var product = new
                        {
                            Name = "Macbook Pro",
                            Price = 2000,
                            Manufacturer = "Apple"
                        };
                        string jsonstring = JsonConvert.SerializeObject(product);
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonstring);
                        response.ContentLength64 = buffer.Length;
                        await outputstream.WriteAsync(buffer, 0, buffer.Length);

                    }
                    break;
                case "/listID":
                    {
                        response.Headers.Add("Content-Type", "application/json");

                        List<string> listID = new List<string> { "123456781234567812345677", 
                            "123456781234567812345678", "123456781234567812345679" };
                        var ids = new
                        {
                            ids = listID
                        };

                        string jsonstring = JsonConvert.SerializeObject(ids);
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(jsonstring);
                        response.ContentLength64 = buffer.Length;
                        await outputstream.WriteAsync(buffer, 0, buffer.Length);

                    }
                    break;
                case "/anh2.png":
                    {
                        response.Headers.Add("Content-Type", "image/png");
                        byte[] buffer = await File.ReadAllBytesAsync("anh2.png");
                        response.ContentLength64 = buffer.Length;
                        await outputstream.WriteAsync(buffer, 0, buffer.Length);

                    }
                    break;

                default:
                    {
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Request Invalid!");
                        response.ContentLength64 = buffer.Length;
                        await outputstream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    break;
            }

            // switch (request.Url.AbsolutePath)


            // Đóng stream để hoàn thành gửi về client
            outputstream.Close();
        }

        // Tạo nội dung HTML trả về cho Client (HTML chứa thông tin về Request)
        public string GenerateHTML(HttpListenerRequest request)
        {
            string format = @"<!DOCTYPE html>
                            <html lang=""en""> 
                                <head>
                                    <meta charset=""UTF-8"">
                                    {0}
                                 </head> 
                                <body>
                                    {1}
                                </body> 
                            </html>";
            string head = "<title>Test WebServer</title>";
            var body = new StringBuilder();
            body.Append("<h1>Request Info</h1>");
            body.Append("<h2>Request Header:</h2>");

            if (request == null || request.Headers == null)
                return "";

            // Header infomation
            var headers = from key in request.Headers.AllKeys
                          select $"<div>{key} : {string.Join(",", request.Headers.GetValues(key))}</div>";
            body.Append(string.Join("", headers));

            //Extract request properties
            body.Append("<h2>Request properties:</h2>");
            var properties = request.GetType().GetProperties();
            foreach (var property in properties)
            {
                var name_pro = property.Name;
                string value_pro;
                try
                {
                    value_pro = property.GetValue(request)?.ToString();
                }
                catch (Exception e)
                {
                    value_pro = e.Message;
                }
                body.Append($"<div>{name_pro} : {value_pro}</div>");

            };
            string html = string.Format(format, head, body.ToString());
            return html;
        }
    }
}




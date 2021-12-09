using gRPCClient.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using russu_vlad_lab10;


namespace gRPCClient.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public async Task<IActionResult> Unary(int id)
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Greeter.GreeterClient(channel);
            var reply = await client.SendStatusAsync(new SRequest { No = id });

            return View("ShowStatus", (object)ChangeToDictionary(reply));
        }

        private Dictionary<string, string> ChangeToDictionary(SResponse response)
        {
            Dictionary<string, string> statusDict = new Dictionary<string, string>();
            foreach (StatusInfo status in response.StatusInfo)
                statusDict.Add(status.Author, status.Description);
            return statusDict;

        }

        public async Task<IActionResult> ServerStreaming(int id)
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Greeter.GreeterClient(channel);

            Dictionary<string, string> statusDict = new Dictionary<string, string>();
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using (var call = client.SendStatusSS(new SRequest { No = id }, cancellationToken:cts.Token))
            {
                try
                {
                    await foreach (var message in call.ResponseStream.ReadAllAsync())
                    {
                        statusDict.Add(message.StatusInfo[0].Author, message.StatusInfo[0].Description);
                    }
                }
                catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
                {
                    // Log stream cancelled
                }
            }

            return View("ShowStatus", (object)statusDict);
        }

        public async Task<IActionResult> ClientStreaming([FromQuery(Name = "st")] int[] statusNo)
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Greeter.GreeterClient(channel);

            Dictionary<string, string> statusDict = new Dictionary<string, string>();
            using (var call = client.SendStatusCS())
            {
                foreach (var st in statusNo)
                {
                    await call.RequestStream.WriteAsync(new SRequest { No = st });
                }
                await call.RequestStream.CompleteAsync();
                SResponse sRes = await call.ResponseAsync;
                foreach (StatusInfo status in sRes.StatusInfo)
                    statusDict.Add(status.Author, status.Description);
            }
            return View("ShowStatus", (object)statusDict);
        }

        public async Task<IActionResult> BiDirectionalStreaming([FromQuery(Name = "st")] int[] statusNo)
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Greeter.GreeterClient(channel);
            Dictionary<string, string> statusDict = new Dictionary<string, string>();
            using (var call = client.SendStatusBD())
            {
                var responseReaderTask = Task.Run(async () =>
                {
                    while (await call.ResponseStream.MoveNext())
                    {
                        var response = call.ResponseStream.Current;
                        foreach (StatusInfo status in response.StatusInfo)
                            statusDict.Add(status.Author, status.Description);
                    }
                });
                //int[] statusNo = { 3, 2, 4 };
                foreach (var st in statusNo)
                {
                    await call.RequestStream.WriteAsync(new SRequest { No = st });
                }
                await call.RequestStream.CompleteAsync();
                await responseReaderTask;
            }
            return View("ShowStatus", (object)statusDict);
        }
    }
}

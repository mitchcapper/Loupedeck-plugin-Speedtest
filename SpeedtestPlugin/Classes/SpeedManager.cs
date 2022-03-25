namespace Loupedeck.SpeedtestPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Threading.Tasks;


    public class SpeedManager
    {

        public async Task<Int64> TestService(IISpeedService service, Boolean uploadTest, Int32 maxServers, Int32 maxTests, Int32 maxConcurrentTests, Int32 mbPerTest, Boolean no_ping_refresh = false)
        {
            if (service.PossibleServers == null)
            {
                await service.RefreshPossibleServers();
            }

            if (!no_ping_refresh)
            {
                await this.RefreshServerPings(service);
            }

            var bytes = service.GetServiceLikeableSize(mbPerTest);
            var urls = this.GetSpeedUrls(service, service.PossibleServers, maxServers, maxTests, bytes, uploadTest);
            var tester = new SpeedTester();
            var res = await tester.GetSpeedBytesPerSec(urls, maxConcurrentTests, ifUploadHowManyBytes: uploadTest ? bytes : 0);
            GC.Collect();
            return res;
        }
           public async Task<(Double min_ping, Double max_ping)> RefreshServerPings(IISpeedService service, Int32 times = 2)
        {
            if (service.PossibleServers == null)
            {
                await service.RefreshPossibleServers();
            }

            service.PossibleServers.ToList().ForEach(a => { a.pingChecks = 0; a.pingAvg = 0; });
            times--;
            await Task.WhenAll(service.PossibleServers.Select(a => this.UpdateServerPing(a)));
            while (times-- > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                await Task.WhenAll(service.PossibleServers.Select(a => this.UpdateServerPing(a)));
            }
            var ret = (min: service.PossibleServers.Min(a => a.pingAvg), max: service.PossibleServers.Max(a => a.pingAvg));

            return ret;
        }
        public async Task<Int64> DoRationalPreTestAndTest(IISpeedService service, Boolean doUpload, Boolean noPingTestFirst = false, Boolean alwaysDoSmallerFinalTest = false)
        {
            //first we do a small test only 6 mb total transferred, if that completes in medium time then we will do a 50 mb final test, if that is fast we can do our 250mb test
            var stopwatch = new Stopwatch();


            var superSlowSeconds = 10; //4.8 megabits a second
            var mediumSeconds = 5; //9.6 megabits a second
            var do_big_final = !alwaysDoSmallerFinalTest;
            if (!noPingTestFirst)
            {
                await this.RefreshServerPings(service);
            }

            stopwatch.Start();
            var res = await this.TestService(service, doUpload, 2, 3, 5, 2, no_ping_refresh: true);
            stopwatch.Stop();
            if (stopwatch.Elapsed.TotalSeconds > mediumSeconds)
            {
                do_big_final = false;
            }

            var finalMaxTests = do_big_final ? 10 : 5;
            var finalMBPerTest = do_big_final ? 25 : 10;
            var finalMaxConcurrent = do_big_final ? 5 : 3;
            if (stopwatch.Elapsed.TotalSeconds <= superSlowSeconds)
            {
                res = await this.TestService(service, doUpload, 3, finalMaxTests, finalMaxConcurrent, finalMBPerTest, no_ping_refresh: true);
            }
            return res;
        }
        public async Task UpdateServerPing(SpeedManager.ServerResult server)
        {
            var ping = new Ping();


            var uri_parse = server.server;
            if (uri_parse.StartsWith("http", StringComparison.OrdinalIgnoreCase) == false)
            {
                uri_parse = "https://" + server.server;
            }

            var uri = new Uri(uri_parse);
            var ip = (await Dns.GetHostEntryAsync(uri.Host)).AddressList[0];
            var reply = await ping.SendPingAsync(ip);
            var time = reply.RoundtripTime + server.pingAvg * server.pingChecks;
            server.pingAvg = time / ++server.pingChecks;



        }
        public IEnumerable<String> GetSpeedUrls(IISpeedService service, IEnumerable<ServerResult> servers, Int32 maxDiffServers, Int32 TotalTests, Int64 BytesPerTest, Boolean isUpload)
        {
            var tryGuid = Guid.NewGuid();
            var ret = new List<String>();
            var test_servers = servers.OrderBy(a => a.pingAvg).Take(Math.Min(maxDiffServers, servers.Count())).ToArray();

            var cur_server = 0;

            while (TotalTests-- > 0)
            {
                ret.Add(service.GetSpeedUrl(test_servers[cur_server++ % test_servers.Length], BytesPerTest, isUpload, tryGuid));
            }
            return ret;
        }
       
        public class ServerResult
        {
            public String server;
            public Int32 pingChecks;
            public Double pingAvg;
        }

    }
}

namespace Loupedeck.SpeedtestPlugin
{

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.Serialization;
    using System.Threading;
    using System.Threading.Tasks;



    public class SpeedTester
    {

        public class SpeedTesterException : Exception
        {
            public String url { get; set; }
            public SpeedTesterException(String url) => this.url = url;

            public SpeedTesterException(String url, String message) : base(message) => this.url = url;

            public SpeedTesterException(String url, String message, Exception innerException) : base(message, innerException) => this.url = url;

            protected SpeedTesterException(String url, SerializationInfo info, StreamingContext context) : base(info, context) => this.url = url;
        }

        public static Int32 TransferBufferSize { get; set; } = (Int32)ByteSize.BytesFromMB(5);

        public async Task<Int64> GetSpeedBytesPerSec(IEnumerable<String> urls, Int32 maxSimultaneous = 5, Int32 timeout = 5000, Int64 ifUploadHowManyBytes = 0)
        {
            var startTime = DateTime.Now;
            Classes.BasicLog.LogEvt($"Running a speed test maxSimultaneous: {maxSimultaneous} timeout: {timeout} ifUploadHowManyBytes: {ifUploadHowManyBytes} servers: {String.Join(", ", urls)}");
            var urlsLeft = new Stack<String>(urls);
            var cur_tasks = new List<Task<Double>>();
            var done_tasks = new List<Task<Double>>();
            while (cur_tasks.Count > 0 || urlsLeft.Count > 0)
            {
                while (cur_tasks.Count < maxSimultaneous && urlsLeft.Count > 0)
                {
                    cur_tasks.Add(this.DoUrl(urlsLeft.Pop(), timeout, (Int32)ifUploadHowManyBytes));
                }
                var res = await Task.WhenAny(cur_tasks);
                cur_tasks.Remove(res);
                done_tasks.Add(res);
            }
            var bytes = done_tasks.Select(a => a.Result).Sum();
            var elapsedSeconds = (DateTime.Now - startTime).TotalSeconds;
            var bytesPerSecond = bytes / elapsedSeconds;

            return (Int32)bytesPerSecond;
        }
        private async Task<Double> DoUrl(String url, Int32 timeout, Int64 ifUploadHowManyBytes = 0) => await this.DoRequestBytes(url, timeout, ifUploadHowManyBytes);


        private static Byte[] _buffer;//way not thread safe warning data corruption between blocks almost quaranteed, oh wait we don't care.....

        private static Byte[] GetBuffer()
        {
            if (_buffer == null)
            {
                _buffer = new Byte[TransferBufferSize];
            }
            return _buffer;
        }
        private class FakeStreamContent : HttpContent
        {

            public FakeStreamContent(Int64 totalLength) => this.totalLength = totalLength;
            private readonly Int64 totalLength;
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                var buffer = GetBuffer();


                var rand = new Random();
                rand.NextBytes(buffer);
                var left = this.totalLength;
                while (true)
                {

                    if (left <= 0)
                    {
                        break;
                    }

                    await stream.WriteAsync(buffer, 0, left > buffer.Length ? buffer.Length : (Int32)left);
                    left -= buffer.Length;
                }


            }

            protected override Boolean TryComputeLength(out Int64 length)
            {
                length = this.totalLength;
                return true;
            }
        }
        public static HttpClient GetNewClient()
        {
            var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.None };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:142.0) Gecko/20100101 Firefox/142.0");
            return client;
        }
        private async Task<Double> DoRequestBytes(String url, Int32 timeout, Int64 ifUploadHowManyBytes = 0)
        {
            try
            {
                var client = GetNewClient();

                _ = this.GetType().Name;
                var totalRead = 0L;
                var buffer = GetBuffer();
                var isMoreToRead = true;
                Classes.BasicLog.LogEvt($"Starting a url test for url: {url} ifUploadHowManyBytes: {ifUploadHowManyBytes}");


                var request = new HttpRequestMessage(ifUploadHowManyBytes != 0 ? HttpMethod.Post : HttpMethod.Get, url);
                if (ifUploadHowManyBytes > 0)
                {
                    var fileContent = new FakeStreamContent(ifUploadHowManyBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    request.Content = fileContent;
                    totalRead += ifUploadHowManyBytes;
                }

                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    var cancellationTokenSource = new CancellationTokenSource();
                    cancellationTokenSource.CancelAfter(timeout);
                    var cancellationToken = cancellationTokenSource.Token;

                    response.EnsureSuccessStatusCode();

                    if (ifUploadHowManyBytes == 0)
                    {
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        {
                            do
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    totalRead += read;
                                }
                            }
                            while (isMoreToRead);
                        }
                    }
                }

                Classes.BasicLog.LogEvt($"Done with a url test for url: {url} read: {totalRead}");
                return totalRead;
            }
            catch (Exception ex)
            {
                throw new SpeedTesterException(url, $"SpeedTester Url Test Failure for url {url}", ex);
            }
        }

    }
}

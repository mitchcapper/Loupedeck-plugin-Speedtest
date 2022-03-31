namespace Loupedeck.SpeedtestPlugin.Commands
{
    using System;
    using System.Text;
    using System.Threading.Tasks;

    public class SpeedtestCommand : PluginDynamicCommand
    {
        private Double _downloadSpeed = -1;
        private Double _uploadSpeed = -1;
        private Int64 _ping = -1;
        private Boolean _isRunning;
        private Boolean _failed;
        private readonly SpeedManager _client;
        private readonly SpeedTestDotNetService speedService;

        public SpeedtestCommand() : base("Speedtest", "Run a speedtest", "Speedtest")
        {
            this._client = new SpeedManager();
            this.speedService = new SpeedTestDotNetService();
        }

        protected override async void RunCommand(String actionParameter)
        {
            if (this._isRunning)
            {
                return;
            }

            // Run in another thread
            await Task.Run(async () =>
            {
                this.Reset();
                this._failed = false;
                this._isRunning = true;
                this.ActionImageChanged();//Immediately inform user test is started to seem responsive

                for (var x = 0; x < 2; x++)//try twice incase of an exception
                {
                    try
                    {
                        // Test ping
                        var (min_ping, max_ping) = await this._client.RefreshServerPings(this.speedService);
                        this._ping = (Int32)Math.Round(min_ping);
                        this.ActionImageChanged();

                        var bytes_sec = await this._client.DoRationalPreTestAndTest(this.speedService, false, true);
                        // Test download speed
                        this._downloadSpeed = bytes_sec;
                        this.ActionImageChanged();

                        // Test upload speed
                        bytes_sec = await this._client.DoRationalPreTestAndTest(this.speedService, true, true);
                        this._uploadSpeed = bytes_sec;
                        this.ActionImageChanged();
                        return;
                    }
                    catch (SpeedManager.SpeedTesterManagerServerException ex)
                    {
                        this._client.AddBadServer(ex.badServer, TimeSpan.FromMinutes(30));
                        Classes.BasicLog.LogEvt(ex, $"Error doing speedtest on try: {x}");
                        if (x == 1)
                        {
                            this._failed = true;
                            this.ActionImageChanged();
                        }
                    }
                }

            });
            this._isRunning = false;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var sb = new StringBuilder();
            var bmpBuilder = new BitmapBuilder(imageSize);
            if (!this._isRunning && this._failed)
            {
                bmpBuilder.DrawText("Test Failed\nTry Again");
                return bmpBuilder.ToImage();
            }

            if (this._ping <= -1)
            {
                bmpBuilder.DrawText(this._isRunning ? "Speedtest started" : "Start speedtest");
                return bmpBuilder.ToImage();
            }

            sb.AppendLine($"Ping: {this._ping} ms");
            sb.AppendLine($"↓ {(this._downloadSpeed <= -1 ? "N/A" : $"{ByteSize.HumanReadable(this._downloadSpeed, 1)}/s")}");
            sb.AppendLine($"↑ {(this._uploadSpeed <= -1 ? "N/A" : $"{ByteSize.HumanReadable(this._uploadSpeed, 1)}/s")}");

            bmpBuilder.DrawText(sb.ToString(), fontSize: 12);
            return bmpBuilder.ToImage();
        }


        private void Reset()
        {
            this._downloadSpeed = -1;
            this._uploadSpeed = -1;
            this._ping = -1;
        }
    }
}
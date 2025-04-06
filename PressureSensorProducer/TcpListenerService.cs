using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PressureSensorProducer
{
    public class TcpListenerService : BackgroundService
    {
        private readonly ILogger<TcpListenerService> _logger;
        private readonly IConfiguration _configuration;
        private InfluxDBClient _influxClient;
        private string _bucket;
        private string _org;
        private readonly IWriteApiAsync _writeApi;

        public TcpListenerService(ILogger<TcpListenerService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Read from config
            var influxUrl = _configuration["InfluxDB:Url"];
            var token = _configuration["InfluxDB:Token"];
            _org = _configuration["InfluxDB:Org"];
            _bucket = _configuration["InfluxDB:Bucket"];

            // Create the InfluxDB client once
            _influxClient = InfluxDBClientFactory.Create(influxUrl, token);

            // Create the WriteApiAsync once
            _writeApi = _influxClient.GetWriteApiAsync();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Listen on TCP port 5000.
            TcpListener listener = new TcpListener(IPAddress.Any, 5000);
            listener.Start();
            _logger.LogInformation("TCP server listening on port 5000.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Accept new client connections.
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            _logger.LogInformation("TCP client connected.");
            using (client)
            {
                var stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead;

                // Read data continuously on this persistent connection.
                while (!stoppingToken.IsCancellationRequested &&
                       (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken)) != 0)
                {
                    var receivedText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    // Assume messages are newline-delimited.
                    foreach (var line in receivedText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = line.Trim();
                        _logger.LogInformation("Received: {Message}", trimmed);

                        // Example expected format: "pressuresSensor_1=123.45"
                        var parts = trimmed.Split('=');
                        if (parts.Length == 2 && parts[0].Trim() == "pressuresSensor_1")
                        {
                            string valueStr = parts[1].Trim();
                            if (double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double sensorValue))
                            {
                                // Write the data point to InfluxDB.
                                await WriteToInfluxDbAsync(sensorValue, stoppingToken);
                            }
                            else
                            {
                                _logger.LogWarning("Invalid sensor value received: {Value}", valueStr);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Received improperly formatted message: {Message}", trimmed);
                        }
                    }
                }
            }
            _logger.LogInformation("TCP client disconnected.");
        }

        private async Task WriteToInfluxDbAsync(double sensorValue, CancellationToken cancellationToken)
        {
            var point = PointData.Measurement("pressure_measurements")
                .Tag("sensor", "pressuresSensor_1")
                .Field("value", sensorValue)
                .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

            try
            {
                // Write the point using your single WriteApiAsync instance
                await _writeApi.WritePointAsync(point, _bucket, _org, cancellationToken);
                _logger.LogInformation("Written value {Value} to InfluxDB.", sensorValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to InfluxDB.");
            }
        }



        public override void Dispose()
        {
            _influxClient?.Dispose();
            base.Dispose();
        }
    }
}

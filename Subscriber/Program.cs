using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StackExchange.Redis;

class Program
{
    // Pre-compile the regex for performance.
    // This pattern matches "pressuresSensor_1" optionally surrounded by spaces, an equal sign, then captures any sequence of characters that are not comma or whitespace.
    static readonly Regex pressureRegex = new Regex(@"pressuresSensor_1\s*=\s*([^,\s]+)", RegexOptions.Compiled);

    static async Task Main(string[] args)
    {
        // Connect to Redis
        string redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(redisConnection);
        IDatabase db = redis.GetDatabase();

        string streamKey = "mystream";
        string consumerGroup = "mygroup";
        string consumerName = "consumer1";

        // Create the consumer group if it doesn't already exist.
        try
        {
            db.StreamCreateConsumerGroup(streamKey, consumerGroup, "$");
            Console.WriteLine("Consumer group created.");
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            Console.WriteLine("Consumer group already exists.");
        }

        Console.WriteLine("Waiting for new messages...");

        while (true)
        {
            var entries = db.StreamReadGroup(
                streamKey, consumerGroup, consumerName, ">",
                count: 1,
                noAck: false
            );

            if (entries.Length == 0)
            {
                await Task.Delay(1000);
                continue;
            }

            foreach (var entry in entries)
            {
                // First, try to read the pressuresSensor_1 field stored explicitly.
                string pressureValue = entry.Values.FirstOrDefault(x => x.Name == "pressuresSensor_1").Value;

                // If the stored value is missing or "N/A", extract it from the raw influx line using our regex.
                if (string.IsNullOrWhiteSpace(pressureValue) || pressureValue == "N/A")
                {
                    string rawLine = entry.Values.FirstOrDefault(x => x.Name == "raw_influx_line").Value;
                    pressureValue = ExtractPressureValue(rawLine) ?? "N/A";
                }

                // Display only the numeric value.
                Console.WriteLine($"Processing message {entry.Id}: pressuresSensor_1 = {pressureValue}");

                try
                {
                    // Use environment variables for TCP connection to pressure service
                    string pressureHost = Environment.GetEnvironmentVariable("PRESSURE_SERVICE_HOST") ?? "localhost";
                    int pressurePort = int.Parse(Environment.GetEnvironmentVariable("PRESSURE_SERVICE_PORT") ?? "5000");

                    // Update the TCP client connection code
                    using (TcpClient tcpClient = new TcpClient(pressureHost, pressurePort))
                    {
                        NetworkStream stream = tcpClient.GetStream();
                        // Format your message (for example: "pressuresSensor_1=123.45")
                        string message = $"pressuresSensor_1={pressureValue}\n";
                        byte[] data = Encoding.UTF8.GetBytes(message);
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending TCP message: {ex.Message}");
                }

      

                // Acknowledge the message.
                db.StreamAcknowledge(streamKey, consumerGroup, entry.Id);
            }
        }
    }

    /// <summary>
    /// Uses a regular expression to extract the value for pressuresSensor_1 from the Influx line.
    /// </summary>
    /// <param name="influxLine">The full Influx line string.</param>
    /// <returns>The value as a string, or null if not found.</returns>
    static string ExtractPressureValue(string influxLine)
    {
        if (string.IsNullOrWhiteSpace(influxLine))
            return null;

        var match = pressureRegex.Match(influxLine);
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }
        return null;
    }
}

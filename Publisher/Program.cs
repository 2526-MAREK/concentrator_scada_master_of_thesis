using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using StackExchange.Redis;

ConnectionMultiplexer redis;
IDatabase db;
const string streamKey = "mystream";

// 1) Connect to Redis
string redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
redis = ConnectionMultiplexer.Connect(redisConnection);
db = redis.GetDatabase();
Console.WriteLine("Connected to Redis.");

// 2) Start TCP listener
int port = int.Parse(Environment.GetEnvironmentVariable("TCP_PORT") ?? "8095");
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"Listening on TCP port {port}...");

// Accept incoming clients in a loop
while (true)
{
    var client = listener.AcceptTcpClient();
    Console.WriteLine("Telegraf connected.");
    ThreadPool.QueueUserWorkItem(HandleClient, client);
}

void HandleClient(object state)
{
    var client = (TcpClient)state;
    using var stream = client.GetStream();
    using var reader = new StreamReader(stream);

    string line;
    try
    {
        while ((line = reader.ReadLine()) != null)
        {
            // Attempt to parse the Influx line to find `pressuresSensor_1`
            var pressureValue = ExtractPressureValue(line);

            // Build Redis fields. We'll store the entire raw line AND the extracted value.
            // If we can't find `pressuresSensor_1`, pressureValue will be null.
            var fields = new NameValueEntry[]
            {
                new NameValueEntry("raw_influx_line", line),
                new NameValueEntry("pressuresSensor_1", pressureValue ?? "N/A")
            };

            var messageId = db.StreamAdd(streamKey, fields);
            Console.WriteLine($"Received & stored: {line}");
        }
    }
    catch (IOException)
    {
        // Client disconnected
        Console.WriteLine("Telegraf disconnected.");
    }
}

string ExtractPressureValue(string influxLine)
{
    // Influx line format (simplified):
    // measurement,tag1=val1,tag2=val2 field1=val1,field2=val2 timestamp
    //
    // Example:
    // opcua,host=debian,id=... pressuresSensor_1=0,Quality="OK (0x0)" 1742746521000000000

    // 1) Split by space into: [measurement/tags, fields, timestamp]
    var parts = influxLine.Split(' ');
    if (parts.Length < 2)
        return null; // Not a valid line

    // The second chunk (index=1) is the fields portion: "pressuresSensor_1=0,Quality="OK (0x0)""
    var fieldsPart = parts[1];

    // 2) Split fields by comma: ["pressuresSensor_1=0", "Quality="OK (0x0)"", ...]
    var fieldPairs = fieldsPart.Split(',');

    foreach (var field in fieldPairs)
    {
        // 3) Split each field by '=': e.g. ["pressuresSensor_1", "0"]
        var kv = field.Split('=');
        if (kv.Length == 2)
        {
            var key = kv[0];
            var val = kv[1];
            if (key == "pressuresSensor_1")
            {
                return val; // Return the raw string (e.g. "0")
            }
        }
    }

    // If we didn't find the key, return null
    return null;
}

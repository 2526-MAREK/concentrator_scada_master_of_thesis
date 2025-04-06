using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PressureSensorProducer
{
    public class Program
    {
        // Add this to Program.cs in the PressureSensorProducer

        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                          .AddEnvironmentVariables("InfluxDB__"); // Add environment variables with InfluxDB__ prefix
                })
                .ConfigureServices((context, services) =>
                {
                    // Register the TCP listener as a background service.
                    services.AddHostedService<TcpListenerService>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}

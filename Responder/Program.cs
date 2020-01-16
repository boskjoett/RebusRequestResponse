using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Polly;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Routing.TypeBased;
using Zylinc.Common.MessageBus.Messages;
using Zylinc.Common.MessageBus.Messages.DataTypes.ConfigurationManager;
using Zylinc.Common.MessageBus.Messages.DataTypes.Organization;
using Zylinc.Common.MessageBus.Messages.RequestResponses.ConfigurationManager;
using Zylinc.Common.MessageBus.Messages.RequestResponses.Organization;

namespace Responder
{
    class Program
    {
        private const string InputQueueName = "RebusResponderApplication";
        private static BuiltinHandlerActivator _activator;
        private static IBus _bus;

        public static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            Console.WriteLine("Rebus responder application started");

            Configuration = LoadConfiguration();

            _activator = new BuiltinHandlerActivator();

            string rabbitMqConnectionString = Configuration.GetConnectionString("RabbitMq");
            ConnectToRebus(rabbitMqConnectionString, _activator);

            // Register handlers for the messages this service must act on.
            _activator.Handle<UserLoginRequest>(async msg =>
            {
                await HandleUserLoginRequest(msg);
            });
            _activator.Handle<ServiceConfigurationRequest>(async msg =>
            {
                await HandleServiceConfigurationRequest(msg);
            });

            // Subscribe to messages we want to handle
            _bus.Subscribe<UserLoginRequest>().Wait();
            _bus.Subscribe<ServiceConfigurationRequest>().Wait();

            if (IsRunningInDocker())
            {
                // Never exit in Docker mode
                new ManualResetEvent(false).WaitOne();
            }
            else
            {
                Console.WriteLine("\nPress a key to exit...");
                Console.ReadKey();
            }

            _bus.Dispose();
            _activator.Dispose();
        }

        private static async Task HandleUserLoginRequest(UserLoginRequest msg)
        {
            Console.WriteLine($"UserLoginRequest received. Request ID: {msg.RequestMessageId}, Email: {msg.Email}");

            Console.WriteLine("Replying with UserLoginResponse");
            await _bus.Reply(new UserLoginResponse(msg.RequestMessageId, LoginResultCode.LoginGranted, "user1", "bcs@zylinc.com", "Bo", "S"), RebusConfiguration.Headers);
        }

        private static async Task HandleServiceConfigurationRequest(ServiceConfigurationRequest msg)
        {
            Console.WriteLine($"ServiceConfigurationRequest received. Request ID: {msg.RequestMessageId}");

            Console.WriteLine("Replying with ServiceConfigurationResponse");
            await _bus.Reply(new ServiceConfigurationResponse(msg.RequestMessageId, new ServiceConfigurationResponseData[0]), RebusConfiguration.Headers);
        }

        private static void ConnectToRebus(string rabbitMqConnectionString, BuiltinHandlerActivator activator)
        {
            Console.WriteLine($"Connecting to Rebus using RabbitMQ connection string: {rabbitMqConnectionString}");

            // Retry forever with a 10 seconds delay
            var policy = Policy
              .Handle<Exception>()
              .WaitAndRetryForever(sleepDurationProvider: (retryCount) =>
              {
                  // Wait 10 seconds between each retry
                  return TimeSpan.FromSeconds(10);
              },
              onRetry: (exception, timeSpan) =>
              {
                  Console.WriteLine($"Rebus client could not connect to {rabbitMqConnectionString}. " +
                                    $"Retrying after {timeSpan.TotalSeconds:n1} seconds. Exception message: {exception.Message}");
              });

            policy.Execute(() =>
            {
                // Action to perform on each retry
                _bus = Configure.With(activator)
                    .Logging(l => l.Console(LogLevel.Info))
                    .Transport(t => t.UseRabbitMq(rabbitMqConnectionString, InputQueueName))
                    .Routing(r => r.TypeBased()
                        .Map<UserLoginResponse>(InputQueueName)
                        .Map<ServiceConfigurationResponse>(InputQueueName))
                    .Start();
            });

            Console.WriteLine("Connected to Rebus");
        }


        private static IConfiguration LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();
            return builder.Build();
        }

        /// <summary>
        /// Determines if running in a Docker container from environment variables.
        /// </summary>
        /// <returns>
        /// True if running in a Docker container. Otherwise: false.
        /// </returns>
        public static bool IsRunningInDocker()
        {
            try
            {
                string dotNetRunningInContainerEnvVariable = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
                if (!string.IsNullOrEmpty(dotNetRunningInContainerEnvVariable))
                {
                    if (bool.TryParse(dotNetRunningInContainerEnvVariable, out bool runningInDocker))
                        return runningInDocker;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}

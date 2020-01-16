using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Polly;
using Rebus;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Routing.TypeBased;
using Zylinc.Common.MessageBus.Messages;
using Zylinc.Common.MessageBus.Messages.DataTypes.ConfigurationManager;
using Zylinc.Common.MessageBus.Messages.RequestResponses.ConfigurationManager;
using Zylinc.Common.MessageBus.Messages.RequestResponses.Organization;

namespace Requester
{
    class Program
    {
        private const string InputQueueName = "RebusRequesterApplication";
        private static BuiltinHandlerActivator _activator;
        private static IBus _bus;

        public static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            Console.WriteLine("Rebus requester application started");

            Configuration = LoadConfiguration();

            //ExampleWithoutRebusAsync().Wait();
            ExampleUsingRebusAsync().Wait();

            _bus.Dispose();
            _activator.Dispose();
        }

        private static async Task ExampleWithoutRebusAsync()
        {
            _activator = new BuiltinHandlerActivator();

            string rabbitMqConnectionString = Configuration.GetConnectionString("RabbitMq");
            ConnectToRebus(rabbitMqConnectionString, _activator);

            // Register handlers for the messages this service must act on.
            _activator.Handle<UserLoginResponse>(async msg =>
            {
                await HandleUserLoginResponse(msg);
            });
            _activator.Handle<ServiceConfigurationResponse>(async msg =>
            {
                await HandleServiceConfigurationResponse(msg);
            });

            // Subscribe to messages we want to handle
            _bus.Subscribe<UserLoginResponse>().Wait();
            _bus.Subscribe<ServiceConfigurationResponse>().Wait();

            while (true)
            {
                Thread.Sleep(4000);

                Guid requestId = Guid.NewGuid();
                Console.WriteLine($"Publishing UserLoginRequest. Request ID: {requestId}");
                await _bus.Publish(new UserLoginRequest(requestId, InputQueueName, "bcs@zylinc.com", "dsfifigfdg"), RebusConfiguration.Headers);

                Thread.Sleep(4000);

                requestId = Guid.NewGuid();
                Console.WriteLine($"Publishing ServiceConfigurationRequest. Request ID: {requestId}");
                ServiceConfigurationBundle[] serviceConfigurationBundles = new ServiceConfigurationBundle[] { new ServiceConfigurationBundle("MyService", "Bundle1") };
                await _bus.Publish(new ServiceConfigurationRequest(requestId, InputQueueName, serviceConfigurationBundles), RebusConfiguration.Headers);
            }
        }


        private static async Task ExampleUsingRebusAsync()
        {
            _activator = new BuiltinHandlerActivator();

            string rabbitMqConnectionString = Configuration.GetConnectionString("RabbitMq");
            ConnectToRebus(rabbitMqConnectionString, _activator);

            // Subscribe to messages we want to handle
            _bus.Subscribe<UserLoginResponse>().Wait();
            _bus.Subscribe<ServiceConfigurationResponse>().Wait();

            while (true)
            {
                Thread.Sleep(4000);

                Guid requestId = Guid.NewGuid();
                Console.WriteLine($"Sending UserLoginRequest request. Request ID: {requestId}");
                UserLoginResponse userLoginResponse = await _bus.SendRequest<UserLoginResponse>(new UserLoginRequest(requestId, InputQueueName, "bcs@zylinc.com", "dsfifigfdg"), RebusConfiguration.Headers, TimeSpan.FromSeconds(10));
                Console.WriteLine($"UserLoginResponse received. Request ID: {userLoginResponse.RequestMessageId}, Email: {userLoginResponse.Email}, ResultCode: {userLoginResponse.ResultCode}");

                Thread.Sleep(4000);

                requestId = Guid.NewGuid();
                Console.WriteLine($"Sending ServiceConfigurationRequest request. Request ID: {requestId}");
                ServiceConfigurationBundle[] serviceConfigurationBundles = new ServiceConfigurationBundle[] { new ServiceConfigurationBundle("MyService", "Bundle1") };
                ServiceConfigurationResponse serviceConfigurationResponse = await _bus.SendRequest<ServiceConfigurationResponse>(new ServiceConfigurationRequest(requestId, InputQueueName, serviceConfigurationBundles), RebusConfiguration.Headers, TimeSpan.FromSeconds(10));
                Console.WriteLine($"ServiceConfigurationResponse received. Request ID: {serviceConfigurationResponse.RequestMessageId}");
            }
        }

        private static async Task HandleUserLoginResponse(UserLoginResponse msg)
        {
            Console.WriteLine($"UserLoginResponse received. Request ID: {msg.RequestMessageId}, Email: {msg.Email}, ResultCode: {msg.ResultCode}");
            await Task.CompletedTask;
        }

        private static async Task HandleServiceConfigurationResponse(ServiceConfigurationResponse msg)
        {
            Console.WriteLine($"ServiceConfigurationResponse received. Request ID: {msg.RequestMessageId}");
            await Task.CompletedTask;
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
                    .Logging(l => l.Console(LogLevel.Debug))
                    .Transport(t => t.UseRabbitMq(rabbitMqConnectionString, InputQueueName))
                    .Options(o => o.EnableSynchronousRequestReply())
                    .Routing(r => r.TypeBased()
                        // NOTE: Request messages must be mapped to the input queue name of the responding service.
                        .Map<UserLoginRequest>("RebusResponderApplication")
                        .Map<ServiceConfigurationRequest>("RebusResponderApplication"))
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
    }
}

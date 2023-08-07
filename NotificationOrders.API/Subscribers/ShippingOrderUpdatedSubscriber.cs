﻿using Newtonsoft.Json;
using NotificationOrders.API.Infrastructure;
using NotificationOrders.API.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace NotificationOrders.API.Subscribers
{
    public class ShippingOrderUpdatedSubscriber : BackgroundService
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private const string Queue = "notifications-service/shipping-order-updated";
        private const string RoutingKeySubscribe = "shipping-order-updated";
        private readonly IServiceProvider _serviceProvider;
        private const string TrackingsExchange = "trackings-service";

        public ShippingOrderUpdatedSubscriber(IServiceProvider serviceProvider)
        {
            var connectionFactory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest"
            };

            _connection = connectionFactory.CreateConnection("shipping-order-updated-consumer");

            _channel = _connection.CreateModel();

            _channel.ExchangeDeclare(TrackingsExchange, "topic", true, false);

            _channel.QueueDeclare(
                queue: Queue,
                durable: true,
                exclusive: false,
                autoDelete: false);

            _channel.QueueBind(Queue, TrackingsExchange, RoutingKeySubscribe);

            _serviceProvider = serviceProvider;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumer = new EventingBasicConsumer(_channel);

            consumer.Received += (sender, eventArgs) =>
            {
                var contentArray = eventArgs.Body.ToArray();
                var contentString = Encoding.UTF8.GetString(contentArray);
                var @event = JsonConvert.DeserializeObject<ShippingOrderUpdatedEvent>(contentString);

                Console.WriteLine($"Message ShippingOrderUpdatedEvent received with Code {@event.TrackingCode}");

                Notify(@event).Wait(stoppingToken);

                _channel.BasicAck(eventArgs.DeliveryTag, false);
            };

            _channel.BasicConsume(Queue, false, consumer);

            return Task.CompletedTask;
        }

        private async Task Notify(ShippingOrderUpdatedEvent @event)
        {
            using var scope = _serviceProvider.CreateScope();

            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var template =
                new ShippingOrderUpdateTemplate(@event.TrackingCode, @event.ContactEmail, @event.Description);

            await notificationService.Send(template);
        }
    }
}
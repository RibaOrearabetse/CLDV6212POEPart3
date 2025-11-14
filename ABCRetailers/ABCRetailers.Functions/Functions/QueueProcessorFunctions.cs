using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using System.Text.Json;
using ABCRetailers.Functions.Entities;
using Microsoft.Extensions.Configuration;

namespace ABCRetailers.Functions.Functions;
public class QueueProcessorFunctions
{
    private readonly string _conn;
    private readonly string _ordersTable;
    private readonly string _productsTable;
    private readonly string _customersTable;

    public QueueProcessorFunctions(IConfiguration cfg)
    {
        _conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _ordersTable = cfg["TABLE_ORDER"] ?? "Order";
        _productsTable = cfg["TABLE_PRODUCT"] ?? "Product";
        _customersTable = cfg["TABLE_CUSTOMER"] ?? "Customer";
    }

    [Function("OrderNotifications_Processor")]
    public async Task OrderNotificationsProcessor(
        [QueueTrigger("%QUEUE_ORDER_NOTIFICATIONS%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("OrderNotifications_Processor");
        log.LogInformation($"Processing order notification: {message}");

        try
        {
            var orderData = JsonSerializer.Deserialize<OrderNotificationMessage>(
                message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (orderData == null)
            {
                log.LogWarning("Failed to deserialize order notification message (null after parse). Skipping.");
                return; // do not throw to avoid poison escalations
            }

            // Update Orders table with the order data
            var ordersTable = new TableClient(_conn, _ordersTable);
            await ordersTable.CreateIfNotExistsAsync();

            var orderEntity = new OrderEntity
            {
                RowKey = orderData.OrderId,
                CustomerId = orderData.CustomerId,
                ProductId = orderData.ProductId,
                ProductName = orderData.ProductName,
                Quantity = orderData.Quantity,
                UnitPrice = orderData.UnitPrice,
                OrderDateUtc = orderData.OrderDateUtc,
                Status = orderData.Status
            };

            await ordersTable.AddEntityAsync(orderEntity);
            log.LogInformation($"Order {orderData.OrderId} added to Orders table successfully");

            // Send notification to customer (simulated)
            log.LogInformation($"Order notification sent to customer {orderData.CustomerName} for order {orderData.OrderId}");
        }
        catch (JsonException jex)
        {
            log.LogError(jex, "JSON parse error for order notification. Message moved/ignored. Error: {Message}", jex.Message);
            // swallow to avoid host crash; message will retry and eventually go to poison by platform
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected error processing order notification: {Message}", ex.Message);
            // rethrow to allow built-in retry semantics
            throw;
        }
    }

    [Function("StockUpdates_Processor")]
    public async Task StockUpdatesProcessor(
        [QueueTrigger("%QUEUE_STOCK_UPDATES%", Connection = "STORAGE_CONNECTION")] string message,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("StockUpdates_Processor");
        log.LogInformation($"Processing stock update: {message}");

        try
        {
            var stockData = JsonSerializer.Deserialize<StockUpdateMessage>(
                message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (stockData == null)
            {
                log.LogWarning("Failed to deserialize stock update message (null after parse). Skipping.");
                return; // do not throw to avoid poison escalations
            }

            // Update Products table with new stock levels
            var productsTable = new TableClient(_conn, _productsTable);
            await productsTable.CreateIfNotExistsAsync();

            try
            {
                var product = await productsTable.GetEntityAsync<ProductEntity>("Product", stockData.ProductId);
                var productEntity = product.Value;
                productEntity.StockAvailable = stockData.NewStock;
                await productsTable.UpdateEntityAsync(productEntity, productEntity.ETag, TableUpdateMode.Replace);
                log.LogInformation($"Stock updated for product {stockData.ProductId}: {stockData.PreviousStock} -> {stockData.NewStock}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update stock for product {ProductId}", stockData.ProductId);
                throw;
            }
        }
        catch (JsonException jex)
        {
            log.LogError(jex, "JSON parse error for stock update. Message moved/ignored. Error: {Message}", jex.Message);
            // swallow to avoid host crash; message will retry and eventually go to poison by platform
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected error processing stock update: {Message}", ex.Message);
            throw;
        }
    }

    public record OrderNotificationMessage(
        string Type,
        string OrderId,
        string CustomerId,
        string CustomerName,
        string ProductId,
        string ProductName,
        int Quantity,
        double UnitPrice,
        double TotalAmount,
        DateTimeOffset OrderDateUtc,
        string Status
    );

    public record StockUpdateMessage(
        string Type,
        string ProductId,
        string ProductName,
        int PreviousStock,
        int NewStock,
        DateTimeOffset UpdatedDateUtc,
        string UpdatedBy
    );
}

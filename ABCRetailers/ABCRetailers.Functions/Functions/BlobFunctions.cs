using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Queues;
using System.Text.Json;


namespace ABCRetailers.Functions.Functions;
public class BlobFunctions
{
    private readonly string _conn;
    private readonly string _productEventsQueue;

    public BlobFunctions(IConfiguration cfg)
    {
        _conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _productEventsQueue = cfg["QUEUE_PRODUCT_EVENTS"] ?? "product-events";
    }

    [Function("OnProductImageUploaded")]
    public void OnProductImageUploaded(
        [BlobTrigger("%BLOB_PRODUCT_IMAGES%/{name}", Connection = "STORAGE_CONNECTION")] Stream blob,
        string name,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("OnProductImageUploaded");
        log.LogInformation($"Product image uploaded: {name}, size={blob.Length} bytes");

        try
        {
            // Send a simple event message so you can see uploads in a queue immediately
            var queue = new QueueClient(_conn, _productEventsQueue, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
            queue.CreateIfNotExists();

            var evt = new
            {
                Type = "ProductImageUploaded",
                FileName = name,
                SizeBytes = blob.Length,
                UploadedAtUtc = DateTimeOffset.UtcNow
            };
            queue.SendMessage(JsonSerializer.Serialize(evt));
            log.LogInformation($"Product image event enqueued to '{_productEventsQueue}'.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to enqueue product image event: {Message}", ex.Message);
        }
    }
}

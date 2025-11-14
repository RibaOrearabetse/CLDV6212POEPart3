using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Storage.Queues;
using System.Text.Json;

namespace ABCRetailers.Functions.Functions;

public class TestFunctions
{
    private readonly string _conn;
    private readonly string _productEventsQueue;

    public TestFunctions(IConfiguration cfg)
    {
        _conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _productEventsQueue = cfg["QUEUE_PRODUCT_EVENTS"] ?? "product-events";
    }

    [Function("Ping_Enqueue")]
    public async Task<HttpResponseData> Ping(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")] HttpRequestData req,
        FunctionContext ctx)
    {
        var log = ctx.GetLogger("Ping_Enqueue");

        var queue = new QueueClient(_conn, _productEventsQueue, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await queue.CreateIfNotExistsAsync();

        var payload = new
        {
            Type = "Ping",
            Message = "Hello from Functions",
            TimestampUtc = DateTimeOffset.UtcNow
        };
        await queue.SendMessageAsync(JsonSerializer.Serialize(payload));
        log.LogInformation("Ping message enqueued to {Queue}", _productEventsQueue);

        var res = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await res.WriteStringAsync($"Enqueued to {_productEventsQueue}.");
        return res;
    }
}




// Services/AzureStorageService.cs
using System.Text.Json;
using System.Text;
using ABCRetailers.Models;
using ABCRetailers.Services;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Files.Shares;
using Azure.Storage.Queues;

namespace ABCRetailers.Services
{
    public class AzureStorageService : IAzureStorageService
    {
        private readonly TableServiceClient _tableServiceClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ShareServiceClient _shareServiceClient;
        private readonly ILogger<AzureStorageService> _logger;

        public AzureStorageService(
            IConfiguration configuration,
            ILogger<AzureStorageService> logger)
        {
            string connectionString = configuration.GetConnectionString("AzureStorage")
                ?? throw new InvalidOperationException("Azure Storage connection string not found");

            _tableServiceClient = new TableServiceClient(connectionString);
            _blobServiceClient = new BlobServiceClient(connectionString);
            _queueServiceClient = new QueueServiceClient(connectionString);
            _shareServiceClient = new ShareServiceClient(connectionString);
            _logger = logger;

            InitializeStorageAsync().Wait();
        }

        private async Task InitializeStorageAsync()
        {
            try
            {
                _logger.LogInformation("Starting Azure Storage initialization...");

                // Create tables
                await _tableServiceClient.CreateTableIfNotExistsAsync("Customers");
                await _tableServiceClient.CreateTableIfNotExistsAsync("Products");
                await _tableServiceClient.CreateTableIfNotExistsAsync("Orders");
                _logger.LogInformation("Tables created successfully");

                // Create blob containers with retry logic
                var productImagesContainer = _blobServiceClient.GetBlobContainerClient("product-images");
                await productImagesContainer.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

                var paymentProofsContainer = _blobServiceClient.GetBlobContainerClient("payment-proofs");
                await paymentProofsContainer.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);

                _logger.LogInformation("Blob containers created successfully");

                // Create queues
                var orderQueue = _queueServiceClient.GetQueueClient("order-notifications");
                await orderQueue.CreateIfNotExistsAsync();

                var stockQueue = _queueServiceClient.GetQueueClient("stock-updates");
                await stockQueue.CreateIfNotExistsAsync();

                _logger.LogInformation("Queues created successfully");

                // Create file share
                var contractsShare = _shareServiceClient.GetShareClient("contracts");
                await contractsShare.CreateIfNotExistsAsync();

                // Create payments directory in contracts share
                var contractsDirectory = contractsShare.GetDirectoryClient("payments");
                await contractsDirectory.CreateIfNotExistsAsync();

                _logger.LogInformation("File shares created successfully");

                _logger.LogInformation("Azure Storage initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Storage: {Message}", ex.Message);
                throw; // Re-throw to make the error visible
            }
        }

        // Table Operations
        public async Task<List<T>> GetAllEntitiesAsync<T>() where T : class, ITableEntity, new()
        {
            var tableName = GetTableName<T>();
            var tableClient = _tableServiceClient.GetTableClient(tableName);
            var entities = new List<T>();

            // Special handling for Product to ensure Price is read correctly from Azure Table Storage
            if (typeof(T) == typeof(Product))
            {
                await foreach (var rawEntity in tableClient.QueryAsync<TableEntity>())
                {
                    var product = new Product
                    {
                        PartitionKey = rawEntity.PartitionKey,
                        RowKey = rawEntity.RowKey,
                        Timestamp = rawEntity.Timestamp,
                        ETag = rawEntity.ETag,
                        Name = rawEntity.GetString("Name") ?? rawEntity.GetString("ProductName") ?? string.Empty,
                        Description = rawEntity.GetString("Description") ?? string.Empty,
                        StockAvailable = rawEntity.GetInt32("StockAvailable") ?? 0,
                        ImageUrl = rawEntity.GetString("ImageUrl") ?? string.Empty
                    };

                    // Handle Price - try different property names and types
                    if (rawEntity.TryGetValue("Price", out var priceValue))
                    {
                        if (priceValue is double priceDouble)
                        {
                            product.Price = priceDouble;
                        }
                        else if (priceValue is decimal priceDecimal)
                        {
                            product.Price = (double)priceDecimal;
                        }
                        else if (priceValue != null)
                        {
                            if (double.TryParse(priceValue.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
                            {
                                product.Price = parsedPrice;
                            }
                        }
                    }

                    entities.Add((T)(object)product);
                }
            }
            else
            {
                await foreach (var entity in tableClient.QueryAsync<T>())
                {
                    entities.Add(entity);
                }
            }

            return entities;
        }

        public async Task<T?> GetEntityAsync<T>(string partitionKey, string rowKey) where T : class, ITableEntity, new()
        {
            var tableName = GetTableName<T>();
            var tableClient = _tableServiceClient.GetTableClient(tableName);

            try
            {
                // Special handling for Product to ensure Price is read correctly
                if (typeof(T) == typeof(Product))
                {
                    var rawEntity = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
                    if (rawEntity.Value == null) return null;

                    var product = new Product
                    {
                        PartitionKey = rawEntity.Value.PartitionKey,
                        RowKey = rawEntity.Value.RowKey,
                        Timestamp = rawEntity.Value.Timestamp,
                        ETag = rawEntity.Value.ETag,
                        Name = rawEntity.Value.GetString("Name") ?? rawEntity.Value.GetString("ProductName") ?? string.Empty,
                        Description = rawEntity.Value.GetString("Description") ?? string.Empty,
                        StockAvailable = rawEntity.Value.GetInt32("StockAvailable") ?? 0,
                        ImageUrl = rawEntity.Value.GetString("ImageUrl") ?? string.Empty
                    };

                    // Handle Price - try different property names and types
                    if (rawEntity.Value.TryGetValue("Price", out var priceValue))
                    {
                        if (priceValue is double priceDouble)
                        {
                            product.Price = priceDouble;
                        }
                        else if (priceValue is decimal priceDecimal)
                        {
                            product.Price = (double)priceDecimal;
                        }
                        else if (priceValue != null)
                        {
                            if (double.TryParse(priceValue.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
                            {
                                product.Price = parsedPrice;
                            }
                        }
                    }

                    return (T)(object)product;
                }
                else
                {
                    var response = await tableClient.GetEntityAsync<T>(partitionKey, rowKey);
                    return response.Value;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<T> AddEntityAsync<T>(T entity) where T : class, ITableEntity
        {
            var tableName = GetTableName<T>();
            var tableClient = _tableServiceClient.GetTableClient(tableName);

            await tableClient.AddEntityAsync(entity);
            return entity;
        }

        public async Task<T> UpdateEntityAsync<T>(T entity) where T : class, ITableEntity
        {
            var tableName = GetTableName<T>();
            var tableClient = _tableServiceClient.GetTableClient(tableName);

            try
            {
                // Use IfMatch condition for optimistic concurrency
                await tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
                return entity;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 412)
            {
                // Precondition failed - entity was modified by another process
                _logger.LogWarning("Entity update failed due to ETag mismatch for {EntityType} with RowKey {RowKey}",
                    typeof(T).Name, entity.RowKey);
                throw new InvalidOperationException("The entity was modified by another process. Please refresh and try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating entity {EntityType} with RowKey {RowKey}: {Message}",
                    typeof(T).Name, entity.RowKey, ex.Message);
                throw;
            }
        }

        public async Task DeleteEntityAsync<T>(string partitionKey, string rowKey) where T : class, ITableEntity, new()
        {
            var tableName = GetTableName<T>();
            var tableClient = _tableServiceClient.GetTableClient(tableName);

            await tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }

        // Blob Operations
        public async Task<string> UploadImageAsync(IFormFile file, string containerName)
        {
            try
            {
                _logger.LogInformation("Starting image upload to container: {ContainerName}", containerName);
                _logger.LogInformation("File details - Name: {FileName}, Size: {Size}, ContentType: {ContentType}",
                    file.FileName, file.Length, file.ContentType);

                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Ensure container exists
                _logger.LogInformation("Creating container {ContainerName} if it doesn't exist", containerName);
                await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);
                _logger.LogInformation("Container {ContainerName} is ready", containerName);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                _logger.LogInformation("Generated filename: {FileName}", fileName);

                var blobClient = containerClient.GetBlobClient(fileName);
                _logger.LogInformation("Blob client created for: {BlobName}", fileName);

                using var stream = file.OpenReadStream();
                _logger.LogInformation("Starting blob upload...");
                await blobClient.UploadAsync(stream, overwrite: true);
                _logger.LogInformation("Blob upload completed successfully");

                var blobUrl = blobClient.Uri.ToString();
                _logger.LogInformation("Image uploaded successfully. URL: {BlobUrl}", blobUrl);

                return blobUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image to container {ContainerName}: {Message}", containerName, ex.Message);
                throw;
            }
        }

        public async Task<string> UploadFileAsync(IFormFile file, string containerName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Ensure container exists
                await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);

                var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{file.FileName}";
                var blobClient = containerClient.GetBlobClient(fileName);

                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, overwrite: true);

                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file to container {ContainerName}: {Message}", containerName, ex.Message);
                throw;
            }
        }

        public async Task DeleteBlobAsync(string blobName, string containerName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            await blobClient.DeleteIfExistsAsync();
        }

        public async Task<byte[]> DownloadBlobAsync(string blobName, string containerName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                if (!await blobClient.ExistsAsync())
                {
                    throw new FileNotFoundException($"Blob {blobName} not found in container {containerName}");
                }

                using var memoryStream = new MemoryStream();
                await blobClient.DownloadToAsync(memoryStream);
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading blob {BlobName} from container {ContainerName}: {Message}", blobName, containerName, ex.Message);
                throw;
            }
        }

        // Queue Operations
        public async Task SendMessageAsync(string queueName, string message)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.SendMessageAsync(message);
        }

        // Function Integration Methods
        public async Task<string> CallFunctionAsync(string functionUrl, object data)
        {
            using var httpClient = new HttpClient();
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(functionUrl, content);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> CallFunctionWithFileAsync(string functionUrl, IFormFile file, Dictionary<string, string> formData)
        {
            using var httpClient = new HttpClient();
            using var form = new MultipartFormDataContent();

            // Add file
            using var fileContent = new StreamContent(file.OpenReadStream());
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            form.Add(fileContent, "file", file.FileName);

            // Add form data
            foreach (var kvp in formData)
            {
                form.Add(new StringContent(kvp.Value), kvp.Key);
            }

            var response = await httpClient.PostAsync(functionUrl, form);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<BlobFileInfo>> ListBlobsAsync(string containerName)
        {
            try
            {
                _logger.LogInformation("Listing blobs from container: {ContainerName}", containerName);
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var blobs = new List<BlobFileInfo>();

                await foreach (var blob in containerClient.GetBlobsAsync())
                {
                    var blobClient = containerClient.GetBlobClient(blob.Name);
                    var properties = await blobClient.GetPropertiesAsync();

                    blobs.Add(new BlobFileInfo
                    {
                        Name = blob.Name,
                        Size = blob.Properties.ContentLength ?? 0,
                        LastModified = blob.Properties.LastModified ?? DateTimeOffset.MinValue,
                        ContentType = blob.Properties.ContentType ?? "application/octet-stream",
                        Url = blobClient.Uri.ToString()
                    });
                }

                _logger.LogInformation("Found {Count} blobs in container {ContainerName}", blobs.Count, containerName);
                return blobs.OrderByDescending(b => b.LastModified).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing blobs from container {ContainerName}: {Message}", containerName, ex.Message);
                return new List<BlobFileInfo>();
            }
        }

        public async Task<bool> VerifyContainerExistsAsync(string containerName)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var exists = await containerClient.ExistsAsync();
                _logger.LogInformation("Container {ContainerName} exists: {Exists}", containerName, exists);
                return exists.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if container {ContainerName} exists: {Message}", containerName, ex.Message);
                return false;
            }
        }

        public async Task<string?> ReceiveMessageAsync(string queueName)
        {
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            var response = await queueClient.ReceiveMessageAsync();

            if (response.Value != null)
            {
                await queueClient.DeleteMessageAsync(response.Value.MessageId, response.Value.PopReceipt);
                return response.Value.MessageText;
            }

            return null;
        }

        // File Share Operations
        public async Task<string> UploadToFileShareAsync(IFormFile file, string shareName, string directoryName = "")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = string.IsNullOrEmpty(directoryName)
                ? shareClient.GetRootDirectoryClient()
                : shareClient.GetDirectoryClient(directoryName);

            await directoryClient.CreateIfNotExistsAsync();

            var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{file.FileName}";
            var fileClient = directoryClient.GetFileClient(fileName);

            using var stream = file.OpenReadStream();
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            return fileName;
        }

        public async Task<byte[]> DownloadFromFileShareAsync(string shareName, string fileName, string directoryName = "")
        {
            var shareClient = _shareServiceClient.GetShareClient(shareName);
            var directoryClient = string.IsNullOrEmpty(directoryName)
                ? shareClient.GetRootDirectoryClient()
                : shareClient.GetDirectoryClient(directoryName);

            var fileClient = directoryClient.GetFileClient(fileName);
            var response = await fileClient.DownloadAsync();

            using var memoryStream = new MemoryStream();
            await response.Value.Content.CopyToAsync(memoryStream);

            return memoryStream.ToArray();
        }

        private static string GetTableName<T>()
        {
            return typeof(T).Name switch
            {
                nameof(CustomerDetails) => "Customers",
                nameof(Product) => "Products",
                nameof(Order) => "Orders",
                _ => typeof(T).Name + "s"
            };
        }
    }
}


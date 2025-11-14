
// Services/IAzureStorageService.cs
using ABCRetailers.Models;
using ABCRetailers.Services;

namespace ABCRetailers.Services
{
    public interface IAzureStorageService
    {
        // Table operations
        Task<List<T>> GetAllEntitiesAsync<T>() where T : class, Azure.Data.Tables.ITableEntity, new();
        Task<T?> GetEntityAsync<T>(string partitionKey, string rowKey) where T : class, Azure.Data.Tables.ITableEntity, new();
        Task<T> AddEntityAsync<T>(T entity) where T : class, Azure.Data.Tables.ITableEntity;
        Task<T> UpdateEntityAsync<T>(T entity) where T : class, Azure.Data.Tables.ITableEntity;
        Task DeleteEntityAsync<T>(string partitionKey, string rowKey) where T : class, Azure.Data.Tables.ITableEntity, new();

        // Blob operations
        Task<string> UploadImageAsync(IFormFile file, string containerName);
        Task<string> UploadFileAsync(IFormFile file, string containerName);
        Task DeleteBlobAsync(string blobName, string containerName);
        Task<byte[]> DownloadBlobAsync(string blobName, string containerName);

        // Queue operations
        Task SendMessageAsync(string queueName, string message);
        Task<string?> ReceiveMessageAsync(string queueName);

        // File Share operations
        Task<string> UploadToFileShareAsync(IFormFile file, string shareName, string directoryName = "");
        Task<byte[]> DownloadFromFileShareAsync(string shareName, string fileName, string directoryName = "");

        // Function Integration operations
        Task<string> CallFunctionAsync(string functionUrl, object data);
        Task<string> CallFunctionWithFileAsync(string functionUrl, IFormFile file, Dictionary<string, string> formData);

        // Blob listing operations
        Task<List<BlobFileInfo>> ListBlobsAsync(string containerName);
        Task<bool> VerifyContainerExistsAsync(string containerName);
    }

    public class BlobFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTimeOffset LastModified { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
using Azure.Storage.Files.Shares;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using ABCRetailers.Functions.Helpers;
using System.Text;

namespace ABCRetailers.Functions.Functions;

public class FileShareFunctions
{
    private readonly string _conn;
    private readonly string _contractsShare;
    private readonly string _paymentsDir;

    public FileShareFunctions(IConfiguration cfg)
    {
        _conn = cfg["STORAGE_CONNECTION"] ?? throw new InvalidOperationException("STORAGE_CONNECTION missing");
        _contractsShare = cfg["FILESHARE_CONTRACTS"] ?? "contracts";
        _paymentsDir = cfg["FILESHARE_DIR_PAYMENTS"] ?? "payments";
    }

    [Function("FileShare_UploadContract")]
    public async Task<HttpResponseData> UploadContract(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "fileshare/contracts")] HttpRequestData req)
    {
        var contentType = req.Headers.TryGetValues("Content-Type", out var ct) ? ct.First() : "";

        if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return HttpJson.Bad(req, "Multipart form data required");

        try
        {
            var form = await MultipartHelper.ParseAsync(req.Body, contentType);
            var file = form.Files.FirstOrDefault(f => f.FieldName == "ContractFile");

            if (file == null || file.Data.Length == 0)
                return HttpJson.Bad(req, "Contract file is required");

            var shareClient = new ShareClient(_conn, _contractsShare);
            await shareClient.CreateIfNotExistsAsync();

            var directoryClient = shareClient.GetDirectoryClient(_paymentsDir);
            await directoryClient.CreateIfNotExistsAsync();

            var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{file.FileName}";
            var fileClient = directoryClient.GetFileClient(fileName);

            await using var stream = file.Data;
            await fileClient.CreateAsync(stream.Length);
            await fileClient.UploadAsync(stream);

            var result = new
            {
                FileName = fileName,
                FilePath = $"{_paymentsDir}/{fileName}",
                Size = file.Data.Length,
                UploadedAt = DateTimeOffset.UtcNow
            };

            return HttpJson.Created(req, result);
        }
        catch (Exception ex)
        {
            return HttpJson.Bad(req, $"Error uploading contract: {ex.Message}");
        }
    }

    [Function("FileShare_DownloadContract")]
    public async Task<HttpResponseData> DownloadContract(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fileshare/contracts/{fileName}")] HttpRequestData req, string fileName)
    {
        try
        {
            var shareClient = new ShareClient(_conn, _contractsShare);
            var directoryClient = shareClient.GetDirectoryClient(_paymentsDir);
            var fileClient = directoryClient.GetFileClient(fileName);

            var response = await fileClient.DownloadAsync();
            var content = new byte[response.Value.ContentLength];
            await response.Value.Content.ReadAsync(content, 0, content.Length);

            var httpResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
            httpResponse.Headers.Add("Content-Type", "application/octet-stream");
            httpResponse.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            await httpResponse.Body.WriteAsync(content);

            return httpResponse;
        }
        catch (Exception ex)
        {
            return HttpJson.NotFound(req, $"Contract file not found: {ex.Message}");
        }
    }

    [Function("FileShare_ListContracts")]
    public async Task<HttpResponseData> ListContracts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fileshare/contracts")] HttpRequestData req)
    {
        try
        {
            var shareClient = new ShareClient(_conn, _contractsShare);
            var directoryClient = shareClient.GetDirectoryClient(_paymentsDir);

            var files = new List<object>();
            await foreach (var file in directoryClient.GetFilesAndDirectoriesAsync())
            {
                if (file.IsDirectory) continue;

                var fileClient = directoryClient.GetFileClient(file.Name);
                var properties = await fileClient.GetPropertiesAsync();

                files.Add(new
                {
                    Name = file.Name,
                    Size = properties.Value.ContentLength,
                    LastModified = properties.Value.LastModified,
                    ContentType = properties.Value.ContentType
                });
            }

            return HttpJson.Ok(req, files);
        }
        catch (Exception ex)
        {
            return HttpJson.Bad(req, $"Error listing contracts: {ex.Message}");
        }
    }

    [Function("FileShare_DeleteContract")]
    public async Task<HttpResponseData> DeleteContract(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "fileshare/contracts/{fileName}")] HttpRequestData req, string fileName)
    {
        try
        {
            var shareClient = new ShareClient(_conn, _contractsShare);
            var directoryClient = shareClient.GetDirectoryClient(_paymentsDir);
            var fileClient = directoryClient.GetFileClient(fileName);

            await fileClient.DeleteIfExistsAsync();
            return HttpJson.NoContent(req);
        }
        catch (Exception ex)
        {
            return HttpJson.Bad(req, $"Error deleting contract: {ex.Message}");
        }
    }
}

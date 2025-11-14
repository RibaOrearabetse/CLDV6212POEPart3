using ABCRetailers.Models;
using ABCRetailers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Azure.Storage.Blobs;
using System.Net;

namespace ABCRetailers.Controllers
{
    [Authorize(Roles = "Admin,Customer")]
    public class UploadController : Controller
    {
        private readonly IAzureStorageService _storageService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UploadController> _logger;
        private BlobServiceClient? _blobServiceClient;

        public UploadController(
            IAzureStorageService storageService,
            IConfiguration configuration,
            ILogger<UploadController> logger)
        {
            _storageService = storageService;
            _configuration = configuration;
            _logger = logger;
        }

        private BlobServiceClient GetBlobServiceClient()
        {
            if (_blobServiceClient == null)
            {
                var connectionString = _configuration.GetConnectionString("AzureStorage")
                    ?? throw new InvalidOperationException("Azure Storage connection string not found");
                _blobServiceClient = new BlobServiceClient(connectionString);
            }
            return _blobServiceClient;
        }

        // GET: Upload
        [HttpGet]
        public async Task<IActionResult> Index(string? orderId = null, string? customerName = null)
        {
            var username = User.Identity?.Name;
            var isAdmin = User.IsInRole("Admin");
            var isCustomer = User.IsInRole("Customer");

            var vm = new FileUploadModel
            {
                OrderId = orderId,
                CustomerName = customerName ?? username
            };

            // Get orders - customers only see their own orders
            var allOrders = await _storageService.GetAllEntitiesAsync<Order>();
            var orders = isCustomer && !string.IsNullOrEmpty(username)
                ? allOrders.Where(o => o.Username == username).ToList()
                : allOrders.ToList();

            var orderItems = orders
                .Select(o => new SelectListItem
                {
                    Value = o.RowKey,
                    Text = $"{o.OrderId} - {o.Username} - {o.ProductName}"
                })
                .ToList();
            ViewBag.Orders = new SelectList(orderItems, "Value", "Text", orderId);

            // Customers only - hide customer dropdown and auto-set to their username
            if (isCustomer && !string.IsNullOrEmpty(username))
            {
                ViewBag.Customers = null; // Hide customer dropdown for customers
                ViewBag.IsCustomer = true;
                ViewBag.CustomerUsername = username;
            }
            else if (isAdmin)
            {
                // Admins can see all customers
                var customers = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                var customerItems = customers
                    .Select(c => new SelectListItem
                    {
                        Value = string.IsNullOrWhiteSpace(c.Username) ? $"{c.Name} {c.Surname}" : c.Username,
                        Text = $"{c.Name} {c.Surname} - {c.Username} - {c.Email}"
                    })
                    .ToList();
                ViewBag.Customers = new SelectList(customerItems, "Value", "Text", customerName);
                ViewBag.IsCustomer = false;
            }

            // Get uploaded files - customers only see their own files
            var uploadedFiles = await GetUploadedFilesAsync();
            if (isCustomer && !string.IsNullOrEmpty(username))
            {
                uploadedFiles = uploadedFiles.Where(f => f.CustomerName == username).ToList();
            }
            ViewBag.UploadedFiles = uploadedFiles;

            return View(vm);
        }

        // POST: Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(FileUploadModel model)
        {
            if (model.ProofOfPayment == null || model.ProofOfPayment.Length == 0)
            {
                ModelState.AddModelError(nameof(model.ProofOfPayment), "Please select a file to upload.");
            }

            var username = User.Identity?.Name;
            var isAdmin = User.IsInRole("Admin");
            var isCustomer = User.IsInRole("Customer");

            // For customers, ensure they can only upload for their own orders
            if (isCustomer && !string.IsNullOrEmpty(username))
            {
                if (!string.IsNullOrEmpty(model.OrderId))
                {
                    var order = await _storageService.GetEntityAsync<Order>("Order", model.OrderId);
                    if (order != null && order.Username != username)
                    {
                        ModelState.AddModelError("", "You can only upload payment proof for your own orders.");
                    }
                }
                // Auto-set customer name to logged-in customer
                model.CustomerName = username;
            }

            if (!ModelState.IsValid)
            {
                // Get orders - customers only see their own orders
                var allOrders = await _storageService.GetAllEntitiesAsync<Order>();
                var orders = isCustomer && !string.IsNullOrEmpty(username)
                    ? allOrders.Where(o => o.Username == username).ToList()
                    : allOrders.ToList();

                var orderItems = orders
                    .Select(o => new SelectListItem
                    {
                        Value = o.RowKey,
                        Text = $"{o.OrderId} - {o.Username} - {o.ProductName}"
                    })
                    .ToList();
                ViewBag.Orders = new SelectList(orderItems, "Value", "Text", model.OrderId);

                // Customers only - hide customer dropdown
                if (isCustomer && !string.IsNullOrEmpty(username))
                {
                    ViewBag.Customers = null;
                    ViewBag.IsCustomer = true;
                    ViewBag.CustomerUsername = username;
                }
                else if (isAdmin)
                {
                    var customers = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                    var customerItems = customers
                        .Select(c => new SelectListItem
                        {
                            Value = string.IsNullOrWhiteSpace(c.Username) ? $"{c.Name} {c.Surname}" : c.Username,
                            Text = $"{c.Name} {c.Surname} - {c.Username} - {c.Email}"
                        })
                        .ToList();
                    ViewBag.Customers = new SelectList(customerItems, "Value", "Text", model.CustomerName);
                    ViewBag.IsCustomer = false;
                }

                // Get uploaded files - customers only see their own files
                var uploadedFiles = await GetUploadedFilesAsync();
                if (isCustomer && !string.IsNullOrEmpty(username))
                {
                    uploadedFiles = uploadedFiles.Where(f => f.CustomerName == username).ToList();
                }
                ViewBag.UploadedFiles = uploadedFiles;

                return View(model);
            }

            try
            {
                // Upload the proof to the private container created during initialization
                // Include order and customer info in filename for better tracking
                // The OrderId comes from the dropdown selection (it's the RowKey/OrderId)
                var originalFileName = model.ProofOfPayment.FileName;
                var fileExtension = Path.GetExtension(originalFileName);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // Use the OrderId from the dropdown (which is the RowKey/OrderId)
                var orderIdPart = !string.IsNullOrEmpty(model.OrderId) ? model.OrderId : "no-order";
                var customerPart = !string.IsNullOrEmpty(model.CustomerName) ? model.CustomerName.Replace(" ", "_") : "unknown";
                var blobFileName = $"{timestamp}_{orderIdPart}_{customerPart}_{originalFileName}";
                
                _logger.LogInformation("Uploading payment proof - OrderId: {OrderId}, CustomerName: {CustomerName}, FileName: {FileName}", 
                    model.OrderId, model.CustomerName, blobFileName);
                
                // Upload with custom filename
                var blobServiceClient = GetBlobServiceClient();
                var containerClient = blobServiceClient.GetBlobContainerClient("payment-proofs");
                await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
                var blobClient = containerClient.GetBlobClient(blobFileName);
                
                using var stream = model.ProofOfPayment.OpenReadStream();
                await blobClient.UploadAsync(stream, overwrite: true);

                // Optionally notify via queue with context
                var message = $"{{\"orderId\":\"{model.OrderId}\",\"customerName\":\"{model.CustomerName}\",\"file\":\"{blobFileName}\"}}";
                await _storageService.SendMessageAsync("order-notifications", message);

                // On payment upload: set order to Processing and deduct inventory
                if (!string.IsNullOrWhiteSpace(model.OrderId))
                {
                    var order = await _storageService.GetEntityAsync<Order>("Order", model.OrderId);
                    if (order != null)
                    {
                        order.Status = "Processing";
                        await _storageService.UpdateEntityAsync(order);

                        var product = await _storageService.GetEntityAsync<Product>("Product", order.ProductId);
                        if (product != null)
                        {
                            var previous = product.StockAvailable;
                            product.StockAvailable = Math.Max(0, product.StockAvailable - order.Quantity);
                            await _storageService.UpdateEntityAsync(product);

                            var stockMsg = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                type = "stock-update",
                                productId = product.ProductId,
                                productName = product.Name,
                                change = -order.Quantity,
                                previous,
                                current = product.StockAvailable,
                                reason = "payment-proof-uploaded",
                                orderId = order.OrderId
                            });
                            await _storageService.SendMessageAsync("stock-updates", stockMsg);
                        }
                    }
                }

                TempData["Success"] = "Proof of payment uploaded successfully.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading proof of payment");
                ModelState.AddModelError(string.Empty, $"Upload failed: {ex.Message}");
                return View(model);
            }
        }

        private async Task<List<UploadedFileInfo>> GetUploadedFilesAsync()
        {
            try
            {
                var blobFiles = await _storageService.ListBlobsAsync("payment-proofs");
                var files = new List<UploadedFileInfo>();

                foreach (var blob in blobFiles)
                {
                    files.Add(new UploadedFileInfo
                    {
                        FileName = blob.Name,
                        FileSize = FormatFileSize(blob.Size),
                        FileSizeBytes = blob.Size,
                        UploadDate = blob.LastModified.DateTime,
                        FileType = blob.ContentType,
                        OrderId = ExtractOrderIdFromFileName(blob.Name),
                        CustomerName = ExtractCustomerFromFileName(blob.Name),
                        BlobUrl = blob.Url
                    });
                }

                return files;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting uploaded files");
                return new List<UploadedFileInfo>();
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string ExtractOrderIdFromFileName(string fileName)
        {
            // Extract order ID from filename
            // Format: timestamp_orderId_customer_originalFileName
            // Handle URL-encoded filenames
            var decodedFileName = WebUtility.UrlDecode(fileName);
            var parts = decodedFileName.Split('_');
            
            if (parts.Length > 1)
            {
                var orderId = parts[1];
                // Skip if it's a placeholder like "no-order"
                if (orderId != "no-order" && !string.IsNullOrWhiteSpace(orderId))
                {
                    return orderId;
                }
            }
            return string.Empty;
        }

        private string ExtractCustomerFromFileName(string fileName)
        {
            // Extract customer info from filename
            // Format: timestamp_orderId_customer_originalFileName
            // Handle URL-encoded filenames
            var decodedFileName = WebUtility.UrlDecode(fileName);
            var parts = decodedFileName.Split('_');
            
            if (parts.Length > 2)
            {
                var customerName = parts[2];
                // Skip if it's a placeholder like "unknown"
                if (customerName != "unknown" && !string.IsNullOrWhiteSpace(customerName))
                {
                    return customerName;
                }
            }
            return string.Empty;
        }

        // GET: Upload/Details/{fileName}
        public async Task<IActionResult> Details(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return NotFound();

            var username = User.Identity?.Name;
            var isAdmin = User.IsInRole("Admin");
            var isCustomer = User.IsInRole("Customer");

            try
            {
                // Get blob file info
                var blobFiles = await _storageService.ListBlobsAsync("payment-proofs");
                var blob = blobFiles.FirstOrDefault(b => b.Name == fileName);
                
                if (blob == null)
                    return NotFound();

                var fileInfo = new UploadedFileInfo
                {
                    FileName = blob.Name,
                    FileSize = FormatFileSize(blob.Size),
                    FileSizeBytes = blob.Size,
                    UploadDate = blob.LastModified.DateTime,
                    FileType = blob.ContentType,
                    OrderId = ExtractOrderIdFromFileName(blob.Name),
                    CustomerName = ExtractCustomerFromFileName(blob.Name),
                    BlobUrl = blob.Url
                };

                // Security: Customers can only view their own files
                if (isCustomer && !string.IsNullOrEmpty(username))
                {
                    if (fileInfo.CustomerName != username)
                    {
                        TempData["Error"] = "You can only view your own payment proofs.";
                        return RedirectToAction("Index");
                    }
                }

                // Try to get order details if OrderId is available
                // The OrderId comes from the dropdown selection when uploading
                Order? order = null;
                CustomerDetails? customer = null;

                if (!string.IsNullOrEmpty(fileInfo.OrderId))
                {
                    // Get order using OrderId (which is the RowKey from the dropdown)
                    // This is the exact order that was selected in the dropdown when uploading
                    order = await _storageService.GetEntityAsync<Order>("Order", fileInfo.OrderId);
                    
                    if (order != null)
                    {
                        // Get customer using CustomerId from the order (which is the customer's RowKey)
                        if (!string.IsNullOrEmpty(order.CustomerId))
                        {
                            customer = await _storageService.GetEntityAsync<CustomerDetails>("Customer", order.CustomerId);
                        }
                        
                        // If customer not found by CustomerId, try to find by username from order
                        if (customer == null && !string.IsNullOrEmpty(order.Username))
                        {
                            var allCustomers = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                            customer = allCustomers.FirstOrDefault(c => c.Username == order.Username);
                        }
                    }
                }

                // Fallback: If customer not found via order, try to find by username from filename
                if (customer == null && !string.IsNullOrEmpty(fileInfo.CustomerName))
                {
                    var allCustomers = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                    customer = allCustomers.FirstOrDefault(c => c.Username == fileInfo.CustomerName);
                }

                // Fallback: If we have a customer but no order, try to find order by customer
                if (order == null && customer != null && !string.IsNullOrEmpty(customer.RowKey))
                {
                    var allOrders = await _storageService.GetAllEntitiesAsync<Order>();
                    order = allOrders.FirstOrDefault(o => o.CustomerId == customer.RowKey);
                }

                ViewBag.Order = order;
                ViewBag.Customer = customer;
                
                _logger.LogInformation("Payment proof details - OrderId: {OrderId}, Order found: {OrderFound}, Customer found: {CustomerFound}", 
                    fileInfo.OrderId, order != null, customer != null);

                return View(fileInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file details for {FileName}", fileName);
                TempData["Error"] = "Error loading file details.";
                return RedirectToAction("Index");
            }
        }

        // GET: Upload/Download/{fileName}
        public async Task<IActionResult> Download(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return NotFound();

            var username = User.Identity?.Name;
            var isAdmin = User.IsInRole("Admin");
            var isCustomer = User.IsInRole("Customer");

            try
            {
                // Get blob file info
                var blobFiles = await _storageService.ListBlobsAsync("payment-proofs");
                var blob = blobFiles.FirstOrDefault(b => b.Name == fileName);
                
                if (blob == null)
                    return NotFound();

                // Security: Customers can only download their own files
                if (isCustomer && !string.IsNullOrEmpty(username))
                {
                    var customerName = ExtractCustomerFromFileName(blob.Name);
                    if (customerName != username)
                    {
                        TempData["Error"] = "You can only download your own payment proofs.";
                        return RedirectToAction("Index");
                    }
                }

                // Download blob
                var fileContent = await _storageService.DownloadBlobAsync(fileName, "payment-proofs");
                var contentType = blob.ContentType ?? "application/octet-stream";

                return File(fileContent, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file {FileName}", fileName);
                TempData["Error"] = "Error downloading file.";
                return RedirectToAction("Index");
            }
        }
    }

    public class UploadedFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime UploadDate { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string BlobUrl { get; set; } = string.Empty;
    }
}




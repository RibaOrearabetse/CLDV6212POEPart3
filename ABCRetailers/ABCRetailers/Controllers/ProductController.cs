using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ABCRetailers.Models;
using ABCRetailers.Services;
using System.Text.Json;

namespace ABCRetailers.Controllers
{
    [Authorize] // Require authentication for all actions
    public class ProductController : Controller
    {
        private readonly IAzureStorageService _storageService;
        private readonly ILogger<ProductController> _logger;

        public ProductController(IAzureStorageService storageService, ILogger<ProductController> logger)
        {
            _storageService = storageService;
            _logger = logger;
        }

        // Allow all authenticated users to view products
        public async Task<IActionResult> Index(string searchTerm)
        {
            var products = await _storageService.GetAllEntitiesAsync<Product>();

            // Debug: Log prices to see what we're getting
            foreach (var p in products.Take(3))
            {
                _logger.LogInformation("Product: {Name}, Price: {Price}, Price type: {Type}", 
                    p.Name, p.Price, p.Price.GetType().Name);
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                products = products.Where(p =>
                    p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            ViewBag.SearchTerm = searchTerm;
            return View(products);
        }

        // Allow all authenticated users to view product details
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var product = await _storageService.GetEntityAsync<Product>("Product", id);
            if (product == null)
                return NotFound();

            return View(product);
        }

        // Admin only - Create product
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Product product, IFormFile? imageFile)
        {
            _logger.LogInformation("Create method called. ImageFile is null: {IsNull}, Length: {Length}",
                imageFile == null, imageFile?.Length ?? 0);

            // Log all form data for debugging
            _logger.LogInformation("Product data - Name: {Name}, Price: {Price}, Stock: {Stock}",
                product.Name, product.Price, product.StockAvailable);

            // Validate that an image file is provided
            if (imageFile == null || imageFile.Length == 0)
            {
                ModelState.AddModelError("", "Please select an image file.");
                _logger.LogWarning("No image file provided");
                return View(product);
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _logger.LogInformation("Uploading image: {FileName}, Size: {Size} bytes", imageFile.FileName, imageFile.Length);
                    var imageUrl = await _storageService.UploadImageAsync(imageFile, "product-images");
                    product.ImageUrl = imageUrl;
                    _logger.LogInformation("Image uploaded successfully. URL: {ImageUrl}", imageUrl);

                    await _storageService.AddEntityAsync(product);
                    _logger.LogInformation("Product saved successfully with ImageUrl: {ImageUrl}", product.ImageUrl);

                    // Verify the container and list its contents
                    var containerExists = await _storageService.VerifyContainerExistsAsync("product-images");
                    _logger.LogInformation("Product-images container exists: {Exists}", containerExists);

                    var blobs = await _storageService.ListBlobsAsync("product-images");
                    _logger.LogInformation("Found {Count} blobs in product-images container", blobs.Count);

                    // Notify inventory queue about new product
                    var inventoryMsg = JsonSerializer.Serialize(new
                    {
                        type = "product-created",
                        productId = product.ProductId,
                        productName = product.Name,
                        stock = product.StockAvailable,
                        price = product.Price
                    });
                    await _storageService.SendMessageAsync("stocksinventory", inventoryMsg);
                    TempData["Success"] = $"Product '{product.Name}' created successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating product: {Message}", ex.Message);
                    ModelState.AddModelError("", $"Error creating product: {ex.Message}");
                }
            }
            else
            {
                _logger.LogWarning("ModelState is invalid. Errors: {Errors}",
                    string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }

            return View(product);
        }

        // Admin only - Edit product
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var product = await _storageService.GetEntityAsync<Product>("Product", id);
            if (product == null)
                return NotFound();

            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(Product product, IFormFile? imageFile)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var originalProduct = await _storageService.GetEntityAsync<Product>("Product", product.RowKey);
                    if (originalProduct == null)
                        return NotFound();

                    originalProduct.Name = product.Name;
                    originalProduct.Description = product.Description;
                    originalProduct.Price = product.Price;
                    originalProduct.StockAvailable = product.StockAvailable;

                    if (imageFile != null && imageFile.Length > 0)
                    {
                        var imageUrl = await _storageService.UploadImageAsync(imageFile, "product-images");
                        originalProduct.ImageUrl = imageUrl;
                    }

                    await _storageService.UpdateEntityAsync(originalProduct);
                    var stockMsg = JsonSerializer.Serialize(new
                    {
                        type = "product-updated",
                        productId = originalProduct.ProductId,
                        productName = originalProduct.Name,
                        stock = originalProduct.StockAvailable
                    });
                    await _storageService.SendMessageAsync("stock-updates", stockMsg);
                    TempData["Success"] = $"Product '{originalProduct.Name}' updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating product");
                    ModelState.AddModelError("", $"Error updating product: {ex.Message}");
                }
            }
            return View(product);
        }

        // Admin only - Delete product
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id, bool? confirm)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var product = await _storageService.GetEntityAsync<Product>("Product", id);
            if (product == null)
                return NotFound();

            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _storageService.DeleteEntityAsync<Product>("Product", id);
                TempData["Success"] = "Product deleted successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error deleting product: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}

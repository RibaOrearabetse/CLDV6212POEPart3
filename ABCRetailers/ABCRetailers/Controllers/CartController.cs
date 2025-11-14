using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ABCRetailers.Data;
using ABCRetailers.Models;
using ABCRetailers.Models.ViewModels;
using ABCRetailers.Services;
using System.Security.Claims;

namespace ABCRetailers.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CartController : Controller
    {
        private readonly AuthDbContext _db;
        private readonly IAzureStorageService _storageService;
        private readonly IFunctionsApi _functionsApi;
        private readonly ILogger<CartController> _logger;

        public CartController(
            AuthDbContext db,
            IAzureStorageService storageService,
            IFunctionsApi functionsApi,
            ILogger<CartController> logger)
        {
            _db = db;
            _storageService = storageService;
            _functionsApi = functionsApi;
            _logger = logger;
        }

        // GET: Cart
        public async Task<IActionResult> Index()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Index", "Login");
            }

            var cartItems = await _db.Cart
                .Where(c => c.CustomerUsername == username)
                .ToListAsync();

            var viewModel = new CartPageViewModel
            {
                Items = new List<CartItemViewModel>()
            };

            foreach (var item in cartItems)
            {
                if (string.IsNullOrEmpty(item.ProductId))
                    continue;

                try
                {
                    var product = await _storageService.GetEntityAsync<Product>("Product", item.ProductId);
                    if (product != null)
                    {
                        viewModel.Items.Add(new CartItemViewModel
                        {
                            CartId = item.Id,
                            ProductId = item.ProductId,
                            ProductName = product.Name,
                            Price = (decimal)product.Price, // Convert double to decimal
                            Quantity = item.Quantity ?? 1,
                            ImageUrl = product.ImageUrl
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading product {ProductId} for cart", item.ProductId);
                }
            }

            // Calculate total from items
            viewModel.Total = viewModel.Items.Sum(i => i.Subtotal);
            return View(viewModel);
        }

        // POST: Cart/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(string productId, int quantity = 1)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                TempData["Error"] = "Please login to add items to cart";
                return RedirectToAction("Index", "Login");
            }

            if (string.IsNullOrEmpty(productId) || quantity < 1)
            {
                TempData["Error"] = "Invalid product or quantity";
                return RedirectToAction("Index", "Product");
            }

            try
            {
                // Check if product exists
                var product = await _storageService.GetEntityAsync<Product>("Product", productId);
                if (product == null)
                {
                    TempData["Error"] = "Product not found";
                    return RedirectToAction("Index", "Product");
                }

                // Check stock availability
                if (product.StockAvailable < quantity)
                {
                    TempData["Error"] = $"Insufficient stock. Available: {product.StockAvailable}";
                    return RedirectToAction("Index", "Product");
                }

                // Check if item already in cart
                var existingItem = await _db.Cart
                    .FirstOrDefaultAsync(c => c.CustomerUsername == username && c.ProductId == productId);

                if (existingItem != null)
                {
                    // Update quantity
                    var newQuantity = (existingItem.Quantity ?? 0) + quantity;
                    if (product.StockAvailable < newQuantity)
                    {
                        TempData["Error"] = $"Cannot add more. Available stock: {product.StockAvailable}";
                        return RedirectToAction("Index", "Product");
                    }
                    existingItem.Quantity = newQuantity;
                    _db.Cart.Update(existingItem);
                    TempData["Success"] = $"Updated quantity of {product.Name} in cart";
                }
                else
                {
                    // Add new item
                    var cartItem = new Cart
                    {
                        CustomerUsername = username,
                        ProductId = productId,
                        Quantity = quantity
                    };
                    _db.Cart.Add(cartItem);
                    TempData["Success"] = $"{product.Name} added to cart successfully!";
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("Product {ProductId} added to cart for user {Username}", productId, username);

                // Redirect to cart page to show the added item
                return RedirectToAction("Index", "Cart");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding product to cart");
                TempData["Error"] = "Error adding product to cart. Please try again.";
                return RedirectToAction("Index", "Product");
            }
        }

        // POST: Cart/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int cartId, int quantity)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Index", "Login");
            }

            if (quantity < 1)
            {
                TempData["Error"] = "Quantity must be at least 1";
                return RedirectToAction("Index");
            }

            var cartItem = await _db.Cart
                .FirstOrDefaultAsync(c => c.Id == cartId && c.CustomerUsername == username);

            if (cartItem == null)
            {
                TempData["Error"] = "Cart item not found";
                return RedirectToAction("Index");
            }

            cartItem.Quantity = quantity;
            _db.Cart.Update(cartItem);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Cart updated";
            return RedirectToAction("Index");
        }

        // POST: Cart/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int cartId)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Index", "Login");
            }

            var cartItem = await _db.Cart
                .FirstOrDefaultAsync(c => c.Id == cartId && c.CustomerUsername == username);

            if (cartItem != null)
            {
                _db.Cart.Remove(cartItem);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Item removed from cart";
            }

            return RedirectToAction("Index");
        }

        // POST: Cart/ConfirmOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmOrder()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Index", "Login");
            }

            var cartItems = await _db.Cart
                .Where(c => c.CustomerUsername == username)
                .ToListAsync();

            if (!cartItems.Any())
            {
                TempData["Error"] = "Your cart is empty";
                return RedirectToAction("Index");
            }

            try
            {
                // Get customer ID
                var customerId = User.FindFirst("CustomerId")?.Value ?? username;

                // Ensure customer exists in CustomerDetails table for admin views
                try
                {
                    var existingCustomer = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                    var customer = existingCustomer.FirstOrDefault(c => c.Username == username);
                    
                    if (customer == null)
                    {
                        // Try to get customer info from Azure Functions API
                        var apiCustomer = await _functionsApi.GetCustomerByUsernameAsync(username);
                        if (apiCustomer != null)
                        {
                            var customerDetails = new CustomerDetails
                            {
                                PartitionKey = "Customer",
                                RowKey = apiCustomer.Id ?? Guid.NewGuid().ToString(),
                                Name = apiCustomer.Name ?? "",
                                Surname = apiCustomer.Surname ?? "",
                                Username = username,
                                Email = apiCustomer.Email ?? "",
                                ShippingAddress = apiCustomer.ShippingAddress ?? ""
                            };
                            await _storageService.AddEntityAsync(customerDetails);
                            customerId = customerDetails.RowKey;
                            _logger.LogInformation("Customer {Username} created in CustomerDetails table during order", username);
                        }
                    }
                    else
                    {
                        customerId = customer.RowKey;
                    }
                }
                catch (Exception customerEx)
                {
                    _logger.LogWarning(customerEx, "Error ensuring customer exists in CustomerDetails for {Username}", username);
                }

                // Create orders for each cart item
                var createdOrders = new List<string>();
                foreach (var cartItem in cartItems)
                {
                    if (string.IsNullOrEmpty(cartItem.ProductId))
                        continue;

                    var product = await _storageService.GetEntityAsync<Product>("Product", cartItem.ProductId);
                    if (product == null)
                    {
                        _logger.LogWarning("Product {ProductId} not found when confirming order", cartItem.ProductId);
                        continue;
                    }

                    // Check stock availability
                    if (product.StockAvailable < (cartItem.Quantity ?? 1))
                    {
                        TempData["Error"] = $"Insufficient stock for {product.Name}. Available: {product.StockAvailable}";
                        return RedirectToAction("Index");
                    }

                    // Create order with status "Submitted"
                    var order = new Order
                    {
                        PartitionKey = "Order",
                        RowKey = Guid.NewGuid().ToString(),
                        CustomerId = customerId,
                        Username = username,
                        ProductId = cartItem.ProductId,
                        ProductName = product.Name,
                        OrderDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                        Quantity = cartItem.Quantity ?? 1,
                        Price = product.Price, // Price is already double
                        Status = "Submitted" // Order created with status
                    };
                    order.TotalPrice = order.Quantity * order.Price;

                    await _storageService.AddEntityAsync(order);
                    createdOrders.Add(order.OrderId);

                    // Deduct stock (if not cancelled)
                    if (!string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        var prev = product.StockAvailable;
                        product.StockAvailable = Math.Max(0, product.StockAvailable - order.Quantity);
                        await _storageService.UpdateEntityAsync(product);

                        // Send stock update message
                        var stockMsg = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            Type = "StockUpdated",
                            ProductId = product.ProductId,
                            ProductName = product.Name,
                            PreviousStock = prev,
                            NewStock = product.StockAvailable,
                            UpdatedDateUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "order-created-from-cart"
                        });
                        await _storageService.SendMessageAsync("stock-updates", stockMsg);
                    }
                }

                // Clear cart after successful order creation
                _db.Cart.RemoveRange(cartItems);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"Order(s) created successfully! Order IDs: {string.Join(", ", createdOrders)}";
                _logger.LogInformation("Order confirmed for user {Username}. Created {Count} orders", username, createdOrders.Count);

                return RedirectToAction("MyOrders", "Order");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming order for user {Username}", username);
                TempData["Error"] = $"Error creating order: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
    }
}


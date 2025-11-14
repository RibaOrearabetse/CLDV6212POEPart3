// Controllers/OrderController.cs
using ABCRetailers.Models;
using ABCRetailers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ABCRetailers.Models.ViewModels;
using Azure;
using System.Text.Json;

namespace ABCRetailers.Controllers
{
    public class OrderController : Controller
    {
        private readonly IAzureStorageService _storageService;

        public OrderController(IAzureStorageService storageService)
        {
            _storageService = storageService;
        }

        // GET: Orders (Admin only)
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            var orders = await _storageService.GetAllEntitiesAsync<Order>();
            return View(orders);
        }

        // GET: Orders/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _storageService.GetEntityAsync<Order>("Order", id);

            if (order == null)
                return NotFound();

            return View(order);
        }

        // GET: Orders/Create
        public async Task<IActionResult> Create()
        {
            var customers = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
            var products = await _storageService.GetAllEntitiesAsync<Product>();

            var vm = new OrderCreateViewModel
            {
                Customers = customers.Select(c => new SelectListItem
                {
                    Value = c.RowKey,
                    Text = string.IsNullOrWhiteSpace(c.Username) ? $"{c.Name} {c.Surname}" : c.Username
                }).ToList(),
                Products = products.Select(p => new SelectListItem
                {
                    Value = p.RowKey,
                    Text = p.Name
                }).ToList(),
                OrderDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc)
            };

            return View(vm);
        }

        // POST: Orders/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OrderCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // repopulate dropdowns
                var customersList = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                var productsList = await _storageService.GetAllEntitiesAsync<Product>();
                model.Customers = customersList.Select(c => new SelectListItem { Value = c.RowKey, Text = string.IsNullOrWhiteSpace(c.Username) ? $"{c.Name} {c.Surname}" : c.Username }).ToList();
                model.Products = productsList.Select(p => new SelectListItem { Value = p.RowKey, Text = p.Name }).ToList();
                return View(model);
            }

            // Fetch related entities
            var product = await _storageService.GetEntityAsync<Product>("Product", model.ProductId);
            var customer = await _storageService.GetEntityAsync<CustomerDetails>("Customer", model.CustomerId);

            if (product == null)
            {
                ModelState.AddModelError(nameof(model.ProductId), "Selected product does not exist.");
            }
            if (customer == null)
            {
                ModelState.AddModelError(nameof(model.CustomerId), "Selected customer does not exist.");
            }
            if (!ModelState.IsValid)
            {
                var customersList2 = await _storageService.GetAllEntitiesAsync<CustomerDetails>();
                var productsList2 = await _storageService.GetAllEntitiesAsync<Product>();
                model.Customers = customersList2.Select(c => new SelectListItem { Value = c.RowKey, Text = string.IsNullOrWhiteSpace(c.Username) ? $"{c.Name} {c.Surname}" : c.Username }).ToList();
                model.Products = productsList2.Select(p => new SelectListItem { Value = p.RowKey, Text = p.Name }).ToList();
                return View(model);
            }

            var order = new Order
            {
                PartitionKey = "Order",
                RowKey = Guid.NewGuid().ToString(),
                CustomerId = model.CustomerId,
                Username = customer!.Username,
                ProductId = model.ProductId,
                ProductName = product!.Name,
                OrderDate = DateTime.SpecifyKind(model.OrderDate, DateTimeKind.Utc),
                Quantity = model.Quantity,
                Price = product.Price, // Price is already double
                Status = model.Status
            };
            order.TotalPrice = order.Quantity * order.Price;

            await _storageService.AddEntityAsync(order);

            // Apply inventory for initial status: deduct unless Cancelled
            if (!string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                var prodForCreate = await _storageService.GetEntityAsync<Product>("Product", order.ProductId);
                if (prodForCreate != null)
                {
                    var prev = prodForCreate.StockAvailable;
                    prodForCreate.StockAvailable = Math.Max(0, prodForCreate.StockAvailable - order.Quantity);
                    await _storageService.UpdateEntityAsync(prodForCreate);

                    var createStockMsg = JsonSerializer.Serialize(new
                    {
                        Type = "StockUpdated",
                        ProductId = prodForCreate.ProductId,
                        ProductName = prodForCreate.Name,
                        PreviousStock = prev,
                        NewStock = prodForCreate.StockAvailable,
                        UpdatedDateUtc = DateTimeOffset.UtcNow,
                        UpdatedBy = $"order-created-{order.Status.ToLower()}"
                    });
                    await _storageService.SendMessageAsync("stock-updates", createStockMsg);
                }
            }

            var orderMsg = JsonSerializer.Serialize(new
            {
                Type = "OrderCreated",
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                CustomerName = order.Username,
                ProductId = order.ProductId,
                ProductName = order.ProductName,
                Quantity = order.Quantity,
                UnitPrice = order.Price,
                TotalAmount = order.TotalPrice,
                OrderDateUtc = DateTimeOffset.UtcNow,
                Status = order.Status
            });
            await _storageService.SendMessageAsync("order-notifications", orderMsg);
            return RedirectToAction(nameof(Index));
        }

        // GET: Orders/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _storageService.GetEntityAsync<Order>("Order", id);
            if (order == null)
                return NotFound();
            var products = await _storageService.GetAllEntitiesAsync<Product>();
            ViewBag.Products = new SelectList(products, nameof(Product.RowKey), nameof(Product.Name), order.ProductId);
            return View(order);
        }

        // POST: Orders/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Order order)
        {
            if (ModelState.IsValid)
            {
                // Load original order for inventory reconciliation
                var existing = await _storageService.GetEntityAsync<Order>("Order", order.RowKey);
                if (existing == null)
                    return NotFound();

                order.PartitionKey = "Order";
                // update product-derived fields based on potentially new product selection
                var newProduct = await _storageService.GetEntityAsync<Product>("Product", order.ProductId);
                if (newProduct != null)
                {
                    order.ProductName = newProduct.Name;
                    order.Price = newProduct.Price; // Price is already double
                }

                // normalize date to UTC and recompute total
                order.OrderDate = DateTime.SpecifyKind(order.OrderDate, DateTimeKind.Utc);
                order.TotalPrice = order.Quantity * order.Price;
                // ignore concurrency on this simple edit path
                order.ETag = ETag.All;
                await _storageService.UpdateEntityAsync(order);

                // Determine how to apply inventory based on status transitions
                bool wasCancelled = string.Equals(existing.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
                bool nowCancelled = string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

                if (wasCancelled && !nowCancelled)
                {
                    // Previously not deducted; deduct now on reactivation
                    if (newProduct != null)
                    {
                        var prev = newProduct.StockAvailable;
                        newProduct.StockAvailable = Math.Max(0, newProduct.StockAvailable - order.Quantity);
                        await _storageService.UpdateEntityAsync(newProduct);
                        var msg = JsonSerializer.Serialize(new
                        {
                            Type = "StockUpdated",
                            ProductId = newProduct.ProductId,
                            ProductName = newProduct.Name,
                            PreviousStock = prev,
                            NewStock = newProduct.StockAvailable,
                            UpdatedDateUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "order-status-reactivated"
                        });
                        await _storageService.SendMessageAsync("stock-updates", msg);
                    }
                }
                else if (!wasCancelled && nowCancelled)
                {
                    // Was deducted; restore stock when cancelling
                    var oldProduct = await _storageService.GetEntityAsync<Product>("Product", existing.ProductId);
                    if (oldProduct != null)
                    {
                        var prev = oldProduct.StockAvailable;
                        oldProduct.StockAvailable = prev + existing.Quantity;
                        await _storageService.UpdateEntityAsync(oldProduct);
                        var msg = JsonSerializer.Serialize(new
                        {
                            Type = "StockUpdated",
                            ProductId = oldProduct.ProductId,
                            ProductName = oldProduct.Name,
                            PreviousStock = prev,
                            NewStock = oldProduct.StockAvailable,
                            UpdatedDateUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "order-cancelled"
                        });
                        await _storageService.SendMessageAsync("stock-updates", msg);
                    }
                }
                else if (!nowCancelled && existing.ProductId != order.ProductId)
                {
                    // Return stock to old product
                    var oldProduct = await _storageService.GetEntityAsync<Product>("Product", existing.ProductId);
                    if (oldProduct != null)
                    {
                        var prev = oldProduct.StockAvailable;
                        oldProduct.StockAvailable = prev + existing.Quantity;
                        await _storageService.UpdateEntityAsync(oldProduct);
                        var restoreMsg = JsonSerializer.Serialize(new
                        {
                            Type = "StockUpdated",
                            ProductId = oldProduct.ProductId,
                            ProductName = oldProduct.Name,
                            PreviousStock = prev,
                            NewStock = oldProduct.StockAvailable,
                            UpdatedDateUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "order-edit-product-change-restore"
                        });
                        await _storageService.SendMessageAsync("stock-updates", restoreMsg);
                    }
                    // Deduct stock from new product
                    if (newProduct != null)
                    {
                        var prev = newProduct.StockAvailable;
                        newProduct.StockAvailable = Math.Max(0, newProduct.StockAvailable - order.Quantity);
                        await _storageService.UpdateEntityAsync(newProduct);
                        var deductMsg = JsonSerializer.Serialize(new
                        {
                            Type = "StockUpdated",
                            ProductId = newProduct.ProductId,
                            ProductName = newProduct.Name,
                            PreviousStock = prev,
                            NewStock = newProduct.StockAvailable,
                            UpdatedDateUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "order-edit-product-change-deduct"
                        });
                        await _storageService.SendMessageAsync("stock-updates", deductMsg);
                    }
                }
                else if (!nowCancelled && existing.Quantity != order.Quantity)
                {
                    // Same product, adjust by delta
                    var delta = order.Quantity - existing.Quantity; // positive means more items ordered
                    var product = await _storageService.GetEntityAsync<Product>("Product", order.ProductId);
                    if (product != null && delta != 0)
                    {
                        var prev = product.StockAvailable;
                        product.StockAvailable = delta > 0
                            ? Math.Max(0, product.StockAvailable - delta)
                            : product.StockAvailable + (-delta);
                        await _storageService.UpdateEntityAsync(product);
                        var adjMsg = JsonSerializer.Serialize(new
                        {
                            Type = "StockUpdated",
                            ProductId = product.ProductId,
                            ProductName = product.Name,
                            PreviousStock = prev,
                            NewStock = product.StockAvailable,
                            UpdatedDateUtc = DateTimeOffset.UtcNow,
                            UpdatedBy = "order-edit-quantity-change"
                        });
                        await _storageService.SendMessageAsync("stock-updates", adjMsg);
                    }
                }

                return RedirectToAction(nameof(Index));
            }
            var products = await _storageService.GetAllEntitiesAsync<Product>();
            ViewBag.Products = new SelectList(products, nameof(Product.RowKey), nameof(Product.Name), order.ProductId);
            return View(order);
        }

        // GET: Orders/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _storageService.GetEntityAsync<Order>("Order", id);
            if (order == null)
                return NotFound();

            return View(order);
        }

        // GET: Orders/UpdateStatus/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _storageService.GetEntityAsync<Order>("Order", id);
            if (order == null)
                return NotFound();

            ViewBag.StatusOptions = new SelectList(new[]
            {
                new { Value = "Submitted", Text = "Submitted" },
                new { Value = "Processing", Text = "Processing" },
                new { Value = "PROCESSED", Text = "PROCESSED" },
                new { Value = "Shipped", Text = "Shipped" },
                new { Value = "Delivered", Text = "Delivered" },
                new { Value = "Cancelled", Text = "Cancelled" }
            }, "Value", "Text", order.Status);

            return View(order);
        }

        // POST: Orders/UpdateStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(string id, string status)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(status))
                return BadRequest();

            var order = await _storageService.GetEntityAsync<Order>("Order", id);
            if (order == null)
                return NotFound();

            var previousStatus = order.Status;
            order.Status = status;
            await _storageService.UpdateEntityAsync(order);

            // Send status update notification
            var statusMsg = JsonSerializer.Serialize(new
            {
                type = "order-status-updated",
                orderId = order.OrderId,
                previousStatus = previousStatus,
                newStatus = status,
                updatedAt = DateTimeOffset.UtcNow,
                customerId = order.CustomerId,
                productName = order.ProductName
            });
            await _storageService.SendMessageAsync("order-notifications", statusMsg);

            TempData["Success"] = $"Order status updated from {previousStatus} to {status}";
            return RedirectToAction(nameof(Index));
        }

        // POST: Orders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            // Restore inventory on delete only if stock was deducted (i.e., not Cancelled)
            var existing = await _storageService.GetEntityAsync<Order>("Order", id);
            if (existing != null && !string.Equals(existing.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                var product = await _storageService.GetEntityAsync<Product>("Product", existing.ProductId);
                if (product != null)
                {
                    var prev = product.StockAvailable;
                    product.StockAvailable = prev + existing.Quantity;
                    await _storageService.UpdateEntityAsync(product);
                    var msg = JsonSerializer.Serialize(new
                    {
                        type = "stock-update",
                        productId = product.ProductId,
                        productName = product.Name,
                        change = existing.Quantity,
                        previous = prev,
                        current = product.StockAvailable,
                        reason = "order-deleted",
                        orderId = existing.OrderId
                    });
                    await _storageService.SendMessageAsync("stock-updates", msg);
                }
            }

            await _storageService.DeleteEntityAsync<Order>("Order", id);
            return RedirectToAction(nameof(Index));
        }

        // GET: Orders/MyOrders (Customer only)
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> MyOrders()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("Index", "Login");
            }

            var allOrders = await _storageService.GetAllEntitiesAsync<Order>();
            var customerOrders = allOrders
                .Where(o => o.Username == username)
                .OrderByDescending(o => o.OrderDate)
                .ToList();

            return View(customerOrders);
        }
    }
}

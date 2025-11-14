using System.Diagnostics;
using ABCRetailers.Models;
using ABCRetailers.Models.ViewModels;
using ABCRetailers.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ABCRetailers.Controllers
{
    public class HomeController : Controller
    {
        private readonly IFunctionsApi _api;
        private readonly IAzureStorageService _storageService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IFunctionsApi api, IAzureStorageService storageService, ILogger<HomeController> logger)
        {
            _api = api;
            _storageService = storageService;
            _logger = logger;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            try
            {
                // Fetch all data directly from Azure Table Storage in parallel
                var productsTask = _storageService.GetAllEntitiesAsync<Product>();
                var customersTask = _storageService.GetAllEntitiesAsync<CustomerDetails>();
                var ordersTask = _storageService.GetAllEntitiesAsync<Order>();

                await Task.WhenAll(productsTask, customersTask, ordersTask);

                var products = await productsTask;
                var customers = await customersTask;
                var orders = await ordersTask;

                // Convert Products to ProductApi for the view model
                var productApis = products.Select(p => new ProductApi
                {
                    Id = p.RowKey,
                    ProductName = p.Name,
                    Description = p.Description,
                    Price = (decimal)p.Price, // Convert double to decimal for ProductApi
                    StockAvailable = p.StockAvailable,
                    ImageUrl = p.ImageUrl
                }).ToList();

                // Get featured products (all products, or first 12 if more than 12)
                var featuredProducts = productApis.Take(12).ToList();

                var viewModel = new HomeViewModel
                {
                    Products = featuredProducts,
                    CustomerCount = customers.Count,
                    ProductCount = products.Count,
                    OrderCount = orders.Count
                };
                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading home page data: {Message}", ex.Message);
                return View(new HomeViewModel 
                { 
                    Products = new List<ProductApi>(),
                    CustomerCount = 0,
                    ProductCount = 0,
                    OrderCount = 0
                });
            }
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminDashboard()
        {
            try
            {
                // Fetch all data directly from Azure Table Storage for real-time updates
                var customersTask = _storageService.GetAllEntitiesAsync<CustomerDetails>();
                var ordersTask = _storageService.GetAllEntitiesAsync<Order>();
                var productsTask = _storageService.GetAllEntitiesAsync<Product>();

                await Task.WhenAll(customersTask, ordersTask, productsTask);

                var customers = await customersTask;
                var orders = await ordersTask;
                var products = await productsTask;

                // Convert to view models for display
                var customerList = customers.Select(c => new Customer
                {
                    Id = c.RowKey,
                    Name = c.Name,
                    Surname = c.Surname,
                    Username = c.Username,
                    Email = c.Email,
                    ShippingAddress = c.ShippingAddress
                }).ToList();

                var orderList = orders.Select(o => new OrderApi
                {
                    Id = o.OrderId,
                    CustomerId = o.CustomerId,
                    ProductId = o.ProductId,
                    ProductName = o.ProductName,
                    Quantity = o.Quantity,
                    TotalAmount = (decimal)o.TotalPrice, // Convert double to decimal
                    Status = o.Status,
                    OrderDateUtc = o.OrderDate
                }).ToList();

                ViewBag.Customers = customerList;
                ViewBag.Orders = orderList;
                ViewBag.Products = products;
                ViewBag.CustomerCount = customers.Count;
                ViewBag.OrderCount = orders.Count;
                ViewBag.ProductCount = products.Count;
                ViewBag.PendingOrders = orders.Count(o => o.Status != "PROCESSED" && o.Status != "Delivered" && o.Status != "Cancelled");
                
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin dashboard");
                ViewBag.Customers = new List<Customer>();
                ViewBag.Orders = new List<OrderApi>();
                ViewBag.CustomerCount = 0;
                ViewBag.OrderCount = 0;
                ViewBag.ProductCount = 0;
                ViewBag.PendingOrders = 0;
                return View();
            }
        }

        [Authorize(Roles = "Customer")]
        public IActionResult CustomerDashboard()
        {
            ViewBag.Username = User.Identity?.Name;
            ViewBag.UserEmail = User.FindFirst("Email")?.Value;
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

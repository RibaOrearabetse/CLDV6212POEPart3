using ABCRetailers.Models;
using ABCRetailers.Models.SqlAuth;
using ABCRetailers.Services;
using ABCRetailers.Data;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ABCRetailers.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CustomerController : Controller
    {
        private readonly IAzureStorageService _storage;
        private readonly AuthDbContext _db;
        private readonly IFunctionsApi _functionsApi;
        private readonly ILogger<CustomerController> _logger;
        private const string Partition = "Customer";

        public CustomerController(
            IAzureStorageService storage,
            AuthDbContext db,
            IFunctionsApi functionsApi,
            ILogger<CustomerController> logger)
        {
            _storage = storage;
            _db = db;
            _functionsApi = functionsApi;
            _logger = logger;
        }

        // GET: /Customer
        public async Task<IActionResult> Index()
        {
            var customers = await _storage.GetAllEntitiesAsync<CustomerDetails>();
            // optional: sort by Surname then Name
            customers = customers
                .OrderBy(c => c.Surname)
                .ThenBy(c => c.Name)
                .ToList();
            return View(customers);
        }

        // GET: /Customer/Details/{id}
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var entity = await _storage.GetEntityAsync<CustomerDetails>(Partition, id);
            if (entity is null) return NotFound();
            return View(entity);
        }

        // GET: /Customer/Create
        public IActionResult Create()
        {
            return View(new CustomerDetails());
        }

        // POST: /Customer/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CustomerDetails model)
        {
            // Basic server-side validation
            if (!ModelState.IsValid)
                return View(model);

            // Validate required fields
            if (string.IsNullOrWhiteSpace(model.Username))
            {
                ModelState.AddModelError("Username", "Username is required.");
                return View(model);
            }

            if (string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError("Email", "Email is required.");
                return View(model);
            }

            try
            {
                // Check if username already exists in SQL database
                if (await _db.Users.AnyAsync(u => u.Username == model.Username))
                {
                    ModelState.AddModelError("Username", "Username already exists. Please choose a different one.");
                    return View(model);
                }

                // 1. Create user in SQL database with default password "password123"
                var user = new User
                {
                    Username = model.Username,
                    PasswordHash = "password123", // Default password for admin-created customers
                    Role = "Customer" // Admin-created customers are always Customers
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync();
                _logger.LogInformation("User {Username} created in SQL database by admin", model.Username);

                // 2. Ensure partition + id are present for CustomerDetails
                model.PartitionKey = Partition;
                if (string.IsNullOrWhiteSpace(model.RowKey))
                    model.RowKey = Guid.NewGuid().ToString();

                // 3. Create customer in Azure Table Storage (CustomerDetails) for admin views
                await _storage.AddEntityAsync(model);
                _logger.LogInformation("Customer {Username} created in Azure Table Storage by admin", model.Username);

                // 4. Create customer in Azure Functions API (non-blocking)
                try
                {
                    var customer = new Models.Customer
                    {
                        Name = model.Name,
                        Surname = model.Surname,
                        Username = model.Username,
                        Email = model.Email,
                        ShippingAddress = model.ShippingAddress
                    };

                    await _functionsApi.CreateCustomerAsync(customer);
                    _logger.LogInformation("Customer {Username} created in Azure Functions by admin", model.Username);
                }
                catch (Exception apiEx)
                {
                    // Log but don't fail creation if API call fails
                    _logger.LogWarning(apiEx, "Failed to create customer in Azure Functions for {Username}, but customer was saved", model.Username);
                }

                TempData["Success"] = $"Customer '{model.Username}' created successfully! Default password: password123";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error during customer creation for {Username}", model.Username);
                ModelState.AddModelError("", "A database error occurred. Please try again.");
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during customer creation for {Username}: {Message}", model.Username, ex.Message);
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                return View(model);
            }
        }

        // GET: /Customer/Edit/{id}
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var entity = await _storage.GetEntityAsync<CustomerDetails>(Partition, id);
            if (entity is null) return NotFound();
            return View(entity);
        }

        // POST: /Customer/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, CustomerDetails model)
        {
            if (id != model.RowKey) return BadRequest();

            // Keep partition key constant
            model.PartitionKey = Partition;

            // Ignore optimistic concurrency for now
            model.ETag = ETag.All;

            if (!ModelState.IsValid)
                return View(model);

            await _storage.UpdateEntityAsync(model);
            return RedirectToAction(nameof(Index));
        }

        // GET: /Customer/Delete/{id}
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var entity = await _storage.GetEntityAsync<CustomerDetails>(Partition, id);
            if (entity is null) return NotFound();
            return View(entity);
        }

        // POST: /Customer/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            await _storage.DeleteEntityAsync<CustomerDetails>(Partition, id);
            return RedirectToAction(nameof(Index));
        }
    }
}

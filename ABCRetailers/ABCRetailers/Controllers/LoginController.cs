using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using ABCRetailers.Data;
using ABCRetailers.Models.SqlAuth;
using ABCRetailers.Models.ViewModels;
using ABCRetailers.Models;
using ABCRetailers.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ABCRetailers.Controllers
{
    public class LoginController : Controller
    {
        private readonly AuthDbContext _db;
        private readonly IFunctionsApi _functionsApi;
        private readonly IAzureStorageService _storageService;
        private readonly ILogger<LoginController> _logger;

        public LoginController(
            AuthDbContext db,
            IFunctionsApi functionsApi,
            IAzureStorageService storageService,
            ILogger<LoginController> logger)
        {
            _db = db;
            _functionsApi = functionsApi;
            _storageService = storageService;
            _logger = logger;
        }

        [AllowAnonymous]
        public IActionResult Index(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Index(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Find user in SQL database
                var user = await _db.Users
                    .FirstOrDefaultAsync(u => u.Username == model.Username);

                if (user == null || user.PasswordHash != model.Password)
                {
                    ModelState.AddModelError("", "Invalid username or password.");
                    return View(model);
                }

                // Validate role selection matches user's actual role
                var selectedRole = model.Role ?? "Customer";
                var userRole = user.Role ?? "Customer";

                // If user selected Admin, validate username starts with "admin"
                if (string.Equals(selectedRole, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    if (!user.Username.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError("", "Only users with usernames starting with 'admin' can login as Admin.");
                        return View(model);
                    }
                    if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError("", "You are not registered as an Admin. Please select Customer role.");
                        return View(model);
                    }
                }

                // If user selected Customer, validate they are actually a customer
                if (string.Equals(selectedRole, "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
                    {
                        ModelState.AddModelError("", "You are registered as an Admin. Please select Admin role to login.");
                        return View(model);
                    }
                }

                // For now, simple password check (later replace with proper hashing)
                // TODO: Implement proper password hashing (BCrypt, PBKDF2, etc.)

                // Try to fetch customer from Azure Functions API (optional - don't block login)
                string customerId = string.Empty;
                try
                {
                    var customer = await _functionsApi.GetCustomerByUsernameAsync(user.Username);
                    if (customer != null)
                    {
                        customerId = customer.Id;
                        _logger.LogInformation("Customer profile found for {Username}", user.Username);
                    }
                    else
                    {
                        // For customers, use username as fallback ID. For admins, this is fine.
                        customerId = user.Username;
                        _logger.LogWarning("Customer profile not found in Azure Functions for {Username}, using username as CustomerId", user.Username);
                    }
                }
                catch (Exception apiEx)
                {
                    // If API call fails, still allow login
                    customerId = user.Username;
                    _logger.LogWarning(apiEx, "Error fetching customer from Azure Functions for {Username}, using username as CustomerId", user.Username);
                }

                // Create claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("CustomerId", customerId)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
                };

                // Sign in
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Store session data
                HttpContext.Session.SetString("Username", user.Username);
                HttpContext.Session.SetString("Role", user.Role);
                HttpContext.Session.SetString("CustomerId", customerId);

                _logger.LogInformation("User {Username} logged in successfully", user.Username);

                // Redirect based on role
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("AdminDashboard", "Home");
                }
                else
                {
                    return RedirectToAction("CustomerDashboard", "Home");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user {Username}", model.Username);
                ModelState.AddModelError("", "An error occurred during login. Please try again.");
                return View(model);
            }
        }

        [AllowAnonymous]
        public IActionResult Register()
        {
            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Check if username already exists
                if (await _db.Users.AnyAsync(u => u.Username == model.Username))
                {
                    ModelState.AddModelError("Username", "Username already exists. Please choose a different one.");
                    return View(model);
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(model.FirstName))
                {
                    ModelState.AddModelError("FirstName", "First Name is required.");
                    return View(model);
                }

                if (string.IsNullOrWhiteSpace(model.Email))
                {
                    ModelState.AddModelError("Email", "Email is required.");
                    return View(model);
                }

                // Security: Only admins can create admin accounts
                var isAdmin = User.IsInRole("Admin");
                var selectedRole = model.Role;
                
                if (string.Equals(selectedRole, "Admin", StringComparison.OrdinalIgnoreCase) && !isAdmin)
                {
                    // Non-admins cannot register as Admin - force to Customer
                    selectedRole = "Customer";
                    _logger.LogWarning("Non-admin user attempted to register as Admin. Role changed to Customer.");
                }

                // Create user in SQL database first
                var user = new User
                {
                    Username = model.Username,
                    PasswordHash = model.Password, // TODO: Hash password properly
                    Role = selectedRole
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync();
                _logger.LogInformation("User {Username} saved to database", model.Username);

                // Create customer in Azure Table Storage (CustomerDetails) for admin views
                // This is critical - customer must appear in admin customer table
                var customerDetails = new CustomerDetails
                {
                    PartitionKey = "Customer",
                    RowKey = Guid.NewGuid().ToString(),
                    Name = model.FirstName,
                    Surname = model.LastName,
                    Username = model.Username,
                    Email = model.Email,
                    ShippingAddress = model.ShippingAddress
                };

                try
                {
                    await _storageService.AddEntityAsync(customerDetails);
                    _logger.LogInformation("Customer {Username} created in Azure Table Storage (CustomerDetails table) - visible in admin customer list", model.Username);
                }
                catch (Exception storageEx)
                {
                    // Log error but don't fail registration - customer can still login
                    // However, they won't appear in admin customer table until manually added
                    _logger.LogError(storageEx, "CRITICAL: Failed to create customer in Azure Table Storage (CustomerDetails) for {Username}. Customer registered but may not appear in admin customer table.", model.Username);
                    // Note: We continue with registration even if this fails
                }

                // Also try to create customer in Azure Functions API (non-blocking)
                try
                {
                    var customer = new Models.Customer
                    {
                        Name = model.FirstName,
                        Surname = model.LastName,
                        Username = model.Username,
                        Email = model.Email,
                        ShippingAddress = model.ShippingAddress
                    };

                    await _functionsApi.CreateCustomerAsync(customer);
                    _logger.LogInformation("Customer {Username} created in Azure Functions", model.Username);
                }
                catch (Exception apiEx)
                {
                    // Log but don't fail registration if API call fails
                    _logger.LogWarning(apiEx, "Failed to create customer in Azure Functions for {Username}, but user was saved to database", model.Username);
                    // User is still registered, just customer profile in Functions might be missing
                }

                TempData["Success"] = "Registration successful! Please login.";
                _logger.LogInformation("New user registered successfully: {Username}", model.Username);

                return RedirectToAction("Index");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error during registration for user {Username}", model.Username);
                ModelState.AddModelError("", "A database error occurred. Please try again or contact support.");
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user {Username}: {Message}", model.Username, ex.Message);
                ModelState.AddModelError("", $"An error occurred during registration: {ex.Message}");
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            _logger.LogInformation("User logged out");
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}


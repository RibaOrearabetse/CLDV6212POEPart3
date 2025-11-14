using ABCRetailers.Services;
using ABCRetailers.Data;
using ABCRetailers.Models.SqlAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register Azure Storage service
builder.Services.AddSingleton<IAzureStorageService, AzureStorageService>();

// Register HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Configure Azure SQL Database for Authentication
var authConnStr = builder.Configuration.GetConnectionString("AuthDb")
    ?? throw new InvalidOperationException("AuthDb connection string not found");
builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlServer(authConnStr);
});

// Configure HttpClient for Azure Functions API
var functionsBaseUrl = builder.Configuration["AzureFunctions:BaseUrl"] ?? string.Empty;
if (!string.IsNullOrWhiteSpace(functionsBaseUrl) && Uri.TryCreate(functionsBaseUrl.TrimEnd('/') + "/api/", UriKind.Absolute, out var baseUri))
{
    builder.Services.AddHttpClient("Functions", (sp, client) =>
    {
        client.BaseAddress = baseUri;
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
else
{
    // Use a placeholder if BaseUrl is not configured
    builder.Services.AddHttpClient("Functions", (sp, client) =>
    {
        client.BaseAddress = new Uri("https://localhost:7071/api/"); // Default for local development
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}

// Register Functions API service
builder.Services.AddScoped<IFunctionsApi, FunctionsApiClient>();

// Configure Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Index";
        options.AccessDeniedPath = "/Login/AccessDenied";
        options.Cookie.Name = "ABCAuthCookie";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    });

// Configure Session
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "ABCSession";
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.IsEssential = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Configure file upload limit
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50 MB
});

var app = builder.Build();

// Ensure database is created and seeded with sample data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AuthDbContext>();
        context.Database.EnsureCreated(); // Creates database and tables if they don't exist
        
        // Seed sample users if they don't exist (check individually to avoid duplicates)
        var customer1Exists = context.Users.Any(u => u.Username == "customer1");
        var admin1Exists = context.Users.Any(u => u.Username == "admin1");
        var adminExists = context.Users.Any(u => u.Username == "admin");

        var usersToAdd = new List<User>();
        
        if (!customer1Exists)
        {
            usersToAdd.Add(new User { Username = "customer1", PasswordHash = "password123", Role = "Customer" });
        }
        
        if (!admin1Exists)
        {
            usersToAdd.Add(new User { Username = "admin1", PasswordHash = "adminpass456", Role = "Admin" });
        }
        
        if (!adminExists)
        {
            usersToAdd.Add(new User { Username = "admin", PasswordHash = "admin456", Role = "Admin" });
        }

        if (usersToAdd.Any())
        {
            context.Users.AddRange(usersToAdd);
            context.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while creating/seeding the database.");
    }
}

// Set culture settings
var culture = new CultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Session must be before Authentication
app.UseSession();

// Authentication and Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

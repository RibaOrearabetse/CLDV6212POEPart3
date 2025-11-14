using System.Net.Http.Json;
using System.Text.Json;
using ABCRetailers.Models;
using Microsoft.Extensions.Logging;

namespace ABCRetailers.Services
{
    public class FunctionsApiClient : IFunctionsApi
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<FunctionsApiClient> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public FunctionsApiClient(IHttpClientFactory httpClientFactory, ILogger<FunctionsApiClient> logger)
        {
            _httpClient = httpClientFactory.CreateClient("Functions");
            _logger = logger;
        }

        // Customers
        public async Task<List<Customer>> GetCustomersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("customers");
                response.EnsureSuccessStatusCode();
                var customers = await response.Content.ReadFromJsonAsync<List<Customer>>(_jsonOptions);
                return customers ?? new List<Customer>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customers");
                return new List<Customer>();
            }
        }

        public async Task<Customer?> GetCustomerAsync(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"customers/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<Customer>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer {Id}", id);
                return null;
            }
        }

        public async Task<Customer?> GetCustomerByUsernameAsync(string username)
        {
            // FIXED: Match by Username instead of Email
            var customers = await GetCustomersAsync();
            return customers.FirstOrDefault(c => 
                c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<Customer> CreateCustomerAsync(Customer c)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("customers", new
                {
                    Name = c.Name,
                    Surname = c.Surname,
                    Username = c.Username,
                    Email = c.Email,
                    ShippingAddress = c.ShippingAddress
                });
                
                if (response.IsSuccessStatusCode)
                {
                    var created = await response.Content.ReadFromJsonAsync<Customer>(_jsonOptions);
                    return created ?? c;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Failed to create customer in Azure Functions. Status: {Status}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    return c; // Return the customer object even if API call failed
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception creating customer in Azure Functions for {Username}", c.Username);
                return c; // Return the customer object even if API call failed
            }
        }

        public async Task<Customer> UpdateCustomerAsync(string id, Customer c)
        {
            var response = await _httpClient.PutAsJsonAsync($"customers/{id}", new
            {
                Name = c.Name,
                Surname = c.Surname,
                Username = c.Username,
                Email = c.Email,
                ShippingAddress = c.ShippingAddress
            });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<Customer>(_jsonOptions) ?? c;
        }

        public async Task DeleteCustomerAsync(string id)
        {
            var response = await _httpClient.DeleteAsync($"customers/{id}");
            response.EnsureSuccessStatusCode();
        }

        // Products
        public async Task<List<ProductApi>> GetProductsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("products");
                response.EnsureSuccessStatusCode();
                var products = await response.Content.ReadFromJsonAsync<List<ProductApi>>(_jsonOptions);
                return products ?? new List<ProductApi>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching products");
                return new List<ProductApi>();
            }
        }

        public async Task<ProductApi?> GetProductAsync(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"products/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ProductApi>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching product {Id}", id);
                return null;
            }
        }

        public async Task<ProductApi> CreateProductAsync(ProductApi p)
        {
            var response = await _httpClient.PostAsJsonAsync("products", p);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ProductApi>(_jsonOptions) ?? p;
        }

        public async Task<ProductApi> UpdateProductAsync(string id, ProductApi p)
        {
            var response = await _httpClient.PutAsJsonAsync($"products/{id}", p);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ProductApi>(_jsonOptions) ?? p;
        }

        public async Task DeleteProductAsync(string id)
        {
            var response = await _httpClient.DeleteAsync($"products/{id}");
            response.EnsureSuccessStatusCode();
        }

        // Orders
        public async Task<List<OrderApi>> GetOrdersAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("orders");
                response.EnsureSuccessStatusCode();
                var orders = await response.Content.ReadFromJsonAsync<List<OrderApi>>(_jsonOptions);
                return orders ?? new List<OrderApi>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching orders");
                return new List<OrderApi>();
            }
        }

        public async Task<OrderApi?> GetOrderAsync(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"orders/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OrderApi>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching order {Id}", id);
                return null;
            }
        }

        public async Task<List<OrderApi>> GetOrdersByCustomerIdAsync(string customerId)
        {
            var orders = await GetOrdersAsync();
            return orders.Where(o => o.CustomerId == customerId).ToList();
        }

        public async Task<OrderApi> CreateOrderAsync(OrderApi o)
        {
            var response = await _httpClient.PostAsJsonAsync("orders", new
            {
                CustomerId = o.CustomerId,
                ProductId = o.ProductId,
                Quantity = o.Quantity
            });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OrderApi>(_jsonOptions) ?? o;
        }

        public async Task<OrderApi> UpdateOrderStatusAsync(string id, string status)
        {
            var response = await _httpClient.PatchAsJsonAsync($"orders/{id}/status", new { Status = status });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OrderApi>(_jsonOptions) ?? new OrderApi { Id = id, Status = status };
        }

        public async Task DeleteOrderAsync(string id)
        {
            var response = await _httpClient.DeleteAsync($"orders/{id}");
            response.EnsureSuccessStatusCode();
        }
    }
}


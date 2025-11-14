using ABCRetailers.Models;

namespace ABCRetailers.Services
{
    public interface IFunctionsApi
    {
        // Customers
        Task<List<Customer>> GetCustomersAsync();
        Task<Customer?> GetCustomerAsync(string id);
        Task<Customer?> GetCustomerByUsernameAsync(string username);
        Task<Customer> CreateCustomerAsync(Customer c);
        Task<Customer> UpdateCustomerAsync(string id, Customer c);
        Task DeleteCustomerAsync(string id);

        // Products
        Task<List<ProductApi>> GetProductsAsync();
        Task<ProductApi?> GetProductAsync(string id);
        Task<ProductApi> CreateProductAsync(ProductApi p);
        Task<ProductApi> UpdateProductAsync(string id, ProductApi p);
        Task DeleteProductAsync(string id);

        // Orders
        Task<List<OrderApi>> GetOrdersAsync();
        Task<OrderApi?> GetOrderAsync(string id);
        Task<List<OrderApi>> GetOrdersByCustomerIdAsync(string customerId);
        Task<OrderApi> CreateOrderAsync(OrderApi o);
        Task<OrderApi> UpdateOrderStatusAsync(string id, string status);
        Task DeleteOrderAsync(string id);
    }
}


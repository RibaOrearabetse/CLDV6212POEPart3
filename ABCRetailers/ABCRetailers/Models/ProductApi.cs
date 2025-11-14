namespace ABCRetailers.Models
{
    public class ProductApi
    {
        public string Id { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockAvailable { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
    }
}


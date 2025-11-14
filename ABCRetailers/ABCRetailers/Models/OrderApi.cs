namespace ABCRetailers.Models
{
    public class OrderApi
    {
        public string Id { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTimeOffset OrderDateUtc { get; set; }
        public string Status { get; set; } = "Submitted";
    }
}


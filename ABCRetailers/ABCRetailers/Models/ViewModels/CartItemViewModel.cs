namespace ABCRetailers.Models.ViewModels
{
    public class CartItemViewModel
    {
        public int CartId { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public decimal Subtotal => Quantity * Price;
    }

    public class CartPageViewModel
    {
        public List<CartItemViewModel> Items { get; set; } = new();
        public decimal Total { get; set; }
    }
}


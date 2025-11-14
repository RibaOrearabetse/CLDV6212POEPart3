using ABCRetailers.Models;

namespace ABCRetailers.Models.ViewModels
{
    public class HomeViewModel
    {
        public List<ProductApi> Products { get; set; } = new();
        public List<Product> FeaturedProducts { get; set; } = new();
        public int CustomerCount { get; set; }
        public int OrderCount { get; set; }
        public int ProductCount { get; set; }
    }
}

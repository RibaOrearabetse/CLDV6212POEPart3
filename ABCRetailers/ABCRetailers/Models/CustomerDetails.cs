using System.Data;
using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ABCRetailers.Models
{
    public class CustomerDetails : ITableEntity
    {
        public string PartitionKey { get; set; } = "Customer";
        public string RowKey { get; set; } = Guid.NewGuid().ToString(); // This will be the CustomerId as a string
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        [Display(Name = "Customer ID")]
        public string CustomerId => RowKey;

        [Required]
        [Display(Name = "First Name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Last Name")]
        public string Surname { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Shipping Address")]
        public string ShippingAddress { get; set; } = string.Empty;
    }
}

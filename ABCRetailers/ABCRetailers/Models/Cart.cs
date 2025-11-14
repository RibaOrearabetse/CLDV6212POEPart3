using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ABCRetailers.Models
{
    [Table("Cart")] // Matches your SQL table name exactly
    public class Cart
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(100)]
        public string? CustomerUsername { get; set; }

        [MaxLength(100)]
        public string? ProductId { get; set; }

        public int? Quantity { get; set; }
    }
}


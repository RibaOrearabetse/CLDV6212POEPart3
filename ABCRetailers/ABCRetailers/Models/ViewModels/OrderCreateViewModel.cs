using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ABCRetailers.Models.ViewModels
{
    public class OrderCreateViewModel
    {
        [Required]
        [Display(Name = "Customer")]
        public string CustomerId { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Product")]
        public string ProductId { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Quantity")]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
        public int Quantity { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Order Date")]
        public DateTime OrderDate { get; set; } = DateTime.Today;

        [Required]
        [Display(Name = "Status")]
        [StringLength(20)]
        public string Status { get; set; } = "Submitted";

        // Dropdown lists
        public IEnumerable<SelectListItem> Customers { get; set; } = new List<SelectListItem>();
        public IEnumerable<SelectListItem> Products { get; set; } = new List<SelectListItem>();
    }
}

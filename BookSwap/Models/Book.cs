using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;

namespace BookSwap.Models
{
    public class Book
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Enter the book title")]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the author")]
        [StringLength(150)]
        public string Author { get; set; } = string.Empty;

        [Required]
        public string Genre { get; set; } = string.Empty;

        [Range(1450, 2100)]
        public int? PublicationYear { get; set; }

        public string? Publisher { get; set; }

        public string? ISBN { get; set; }

        public int? Pages { get; set; }

        public string? Language { get; set; }

        public string? Description { get; set; }

        public string? CoverImageUrl { get; set; }

        [Required]
        public string Condition { get; set; } = "Good";

        public string? City { get; set; }

        public string ExchangeType { get; set; } = "BookForBook";

        public bool IsAvailable { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string OwnerId { get; set; } = string.Empty;
        public IdentityUser? Owner { get; set; }
    }
}
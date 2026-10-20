
using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace BookSwap.Models
{
    public class SwapRequest
    {
        public int Id { get; set; }

        [Required]
        public string SenderId { get; set; } = string.Empty;

        public IdentityUser Sender { get; set; } = null!;

        [Required]
        public string ReceiverId { get; set; } = string.Empty;

        public IdentityUser Receiver { get; set; } = null!;

        public int RequestedBookId { get; set; }

        public Book RequestedBook { get; set; } = null!;

        public int OfferedBookId { get; set; }

        public Book OfferedBook { get; set; } = null!;

        [Required]
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool SenderConfirmed { get; set; } = false;

        public bool ReceiverConfirmed { get; set; } = false;

        public DateTime? CompletedAt { get; set; }
    }
}

using BookSwap.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookSwap.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
        public DbSet<Book> Books { get; set; }
        public DbSet<SwapRequest> SwapRequests { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Book>()
                .HasOne(b => b.Owner)
                .WithMany()
                .HasForeignKey(b => b.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SwapRequest>(entity =>
            {
                entity.HasOne(s => s.Sender)
                    .WithMany()
                    .HasForeignKey(s => s.SenderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.Receiver)
                    .WithMany()
                    .HasForeignKey(s => s.ReceiverId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.RequestedBook)
                    .WithMany()
                    .HasForeignKey(s => s.RequestedBookId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.OfferedBook)
                    .WithMany()
                    .HasForeignKey(s => s.OfferedBookId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        }
    }
}


using BookSwap.Data;
using BookSwap.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BookSwap.Controllers
{
    [Authorize]
    public class SwapRequestsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SwapRequestsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Create(int bookId)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var requestedBook = await _context.Books
                .FirstOrDefaultAsync(b =>
                    b.Id == bookId && b.IsAvailable);

            if (requestedBook == null)
                return NotFound();

            if (requestedBook.OwnerId == userId)
                return BadRequest("You cannot swap your own book.");

            var myBooks = await _context.Books
                .Where(b => b.OwnerId == userId && b.IsAvailable)
                .OrderBy(b => b.Title)
                .ToListAsync();

            ViewBag.RequestedBook = requestedBook;

            return View(myBooks);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int requestedBookId, int offeredBookId)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var requestedBook = await _context.Books
                .FirstOrDefaultAsync(b =>
                    b.Id == requestedBookId && b.IsAvailable);

            var offeredBook = await _context.Books
                .FirstOrDefaultAsync(b =>
                    b.Id == offeredBookId &&
                    b.OwnerId == userId &&
                    b.IsAvailable);

            if (requestedBook == null || offeredBook == null)
                return BadRequest("Invalid books selected.");

            if (requestedBook.OwnerId == userId)
                return BadRequest("You cannot swap your own book.");

            bool alreadyExists = await _context.SwapRequests
                .AnyAsync(s =>
                    s.SenderId == userId &&
                    s.RequestedBookId == requestedBookId &&
                    s.OfferedBookId == offeredBookId &&
                    s.Status == "Pending");

            if (alreadyExists)
            {
                TempData["Error"] =
                    "You already have a pending request for this swap.";

                return RedirectToAction(
                    nameof(Create),
                    new { bookId = requestedBookId });
            }

            var request = new SwapRequest
            {
                SenderId = userId!,
                ReceiverId = requestedBook.OwnerId,
                RequestedBookId = requestedBookId,
                OfferedBookId = offeredBookId,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.SwapRequests.Add(request);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Swap request sent successfully!";

            return RedirectToAction(nameof(Sent));
        }

        public async Task<IActionResult> Sent()
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var requests = await _context.SwapRequests
                .Include(s => s.RequestedBook)
                .Include(s => s.OfferedBook)
                .Where(s => s.SenderId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return View(requests);
        }

        public async Task<IActionResult> Incoming()
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var requests = await _context.SwapRequests
                .Include(s => s.RequestedBook)
                .Include(s => s.OfferedBook)
                .Where(s => s.ReceiverId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(int id)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var request = await _context.SwapRequests
                .Include(s => s.RequestedBook)
                .Include(s => s.OfferedBook)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (request == null)
                return NotFound();

            if (request.ReceiverId != userId)
                return Forbid();

            if (request.Status != "Pending")
                return BadRequest("This request is no longer pending.");

            if (!request.RequestedBook.IsAvailable ||
                !request.OfferedBook.IsAvailable ||
                request.RequestedBook.OwnerId != request.ReceiverId ||
                request.OfferedBook.OwnerId != request.SenderId)
            {
                return BadRequest("Books are no longer available for this swap.");
            }

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

            try
            {
                // Перевіряємо актуальний стан книг у транзакції.
                await _context.Entry(request.RequestedBook).ReloadAsync();
                await _context.Entry(request.OfferedBook).ReloadAsync();
                await _context.Entry(request).ReloadAsync();

                if (request.Status != "Pending" ||
                    !request.RequestedBook.IsAvailable ||
                    !request.OfferedBook.IsAvailable ||
                    request.RequestedBook.OwnerId != request.ReceiverId ||
                    request.OfferedBook.OwnerId != request.SenderId)
                {
                    await transaction.RollbackAsync();
                    return BadRequest("This swap is no longer available.");
                }

                request.Status = "Accepted";

                // Резервуємо книги для погодженого обміну.
                request.RequestedBook.IsAvailable = false;
                request.OfferedBook.IsAvailable = false;

                var conflictingRequests = await _context.SwapRequests
    .Where(s =>
        s.Id != request.Id &&
        s.Status == "Pending" &&
        (
            s.RequestedBookId == request.RequestedBookId ||
            s.OfferedBookId == request.RequestedBookId ||
            s.RequestedBookId == request.OfferedBookId ||
            s.OfferedBookId == request.OfferedBookId
        ))
    .ToListAsync();

                foreach (var conflicting in conflictingRequests)
                {
                    conflicting.Status = "Declined";
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return Conflict("The swap could not be accepted. Please try again.");
            }
            catch (Npgsql.NpgsqlException ex)
                when (ex.SqlState == "40001")
            {
                await transaction.RollbackAsync();
                return Conflict("The books were updated. Please try again.");
            }

            TempData["Success"] = "Swap request accepted!";

            return RedirectToAction(nameof(Incoming));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Decline(int id)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var request = await _context.SwapRequests
                .FirstOrDefaultAsync(s => s.Id == id);

            if (request == null)
                return NotFound();

            if (request.ReceiverId != userId)
                return Forbid();

            if (request.Status != "Pending")
                return BadRequest("This request is no longer pending.");

            request.Status = "Declined";

            await _context.SaveChangesAsync();

            TempData["Success"] = "Swap request declined.";

            return RedirectToAction(nameof(Incoming));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var request = await _context.SwapRequests
                .FirstOrDefaultAsync(s => s.Id == id);

            if (request == null)
                return NotFound();

            if (request.SenderId != userId)
                return Forbid();

            if (request.Status != "Pending")
                return BadRequest("Only pending requests can be cancelled.");

            request.Status = "Cancelled";

            await _context.SaveChangesAsync();

            TempData["Success"] = "Swap request cancelled.";

            return RedirectToAction(nameof(Sent));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(int id)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

            var request = await _context.SwapRequests
                .FirstOrDefaultAsync(s => s.Id == id);

            if (request == null)
                return NotFound();

            if (request.SenderId != userId &&
                request.ReceiverId != userId)
                return Forbid();

            if (request.Status != "Accepted")
                return BadRequest("Only accepted swaps can be confirmed.");

            if (request.SenderId == userId)
                request.SenderConfirmed = true;

            if (request.ReceiverId == userId)
                request.ReceiverConfirmed = true;

            if (request.SenderConfirmed &&
                request.ReceiverConfirmed)
            {
                request.Status = "Completed";
                request.CompletedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] =
                "Swap confirmation saved successfully!";

            return RedirectToAction(
                request.SenderId == userId
                    ? nameof(Sent)
                    : nameof(Incoming));
        }

    }
}

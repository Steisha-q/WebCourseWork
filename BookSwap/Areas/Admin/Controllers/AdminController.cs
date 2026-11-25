using BookSwap.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookSwap.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        public AdminController(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        public async Task<IActionResult> Dashboard()
        {
            var totalUsers       = await _userManager.Users.CountAsync();
            var totalBooks       = await _context.Books.CountAsync();
            var totalSwaps       = await _context.SwapRequests.CountAsync();
            var completedSwaps   = await _context.SwapRequests.CountAsync(s => s.Status == "Completed");
            var pendingSwaps     = await _context.SwapRequests.CountAsync(s => s.Status == "Pending");
            var availableBooks   = await _context.Books.CountAsync(b => b.IsAvailable);

            var since = DateTime.UtcNow.AddDays(-7);
            var newUsersWeek  = await _userManager.Users.CountAsync(u => u.LockoutEnd == null); 
            var newBooksWeek  = await _context.Books.CountAsync(b => b.CreatedAt >= since);
            var newSwapsWeek  = await _context.SwapRequests.CountAsync(s => s.CreatedAt >= since);

            var recentSwaps = await _context.SwapRequests
                .Include(s => s.Sender)
                .Include(s => s.Receiver)
                .Include(s => s.RequestedBook)
                .Include(s => s.OfferedBook)
                .OrderByDescending(s => s.CreatedAt)
                .Take(10)
                .ToListAsync();

            ViewBag.TotalUsers     = totalUsers;
            ViewBag.TotalBooks     = totalBooks;
            ViewBag.TotalSwaps     = totalSwaps;
            ViewBag.CompletedSwaps = completedSwaps;
            ViewBag.PendingSwaps   = pendingSwaps;
            ViewBag.AvailableBooks = availableBooks;
            ViewBag.NewBooksWeek   = newBooksWeek;
            ViewBag.NewSwapsWeek   = newSwapsWeek;
            ViewBag.RecentSwaps    = recentSwaps;

            return View();
        }

        public async Task<IActionResult> Users(string? search)
        {
            var usersQuery = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                usersQuery = usersQuery.Where(u =>
                    u.Email!.Contains(search) ||
                    u.UserName!.Contains(search));
            }

            var users = await usersQuery
                .OrderBy(u => u.Email)
                .ToListAsync();

            var allBookOwners = await _context.Books
                .GroupBy(b => b.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToListAsync();

            var allSwapSenders = await _context.SwapRequests
                .GroupBy(s => s.SenderId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();

            var bookCountMap  = allBookOwners.ToDictionary(x => x.OwnerId, x => x.Count);
            var swapCountMap  = allSwapSenders.ToDictionary(x => x.UserId, x => x.Count);

            // Determine which users are admins
            var adminUserIds = new HashSet<string>();
            foreach (var u in users)
            {
                if (await _userManager.IsInRoleAsync(u, "Admin"))
                    adminUserIds.Add(u.Id);
            }

            ViewBag.Search       = search;
            ViewBag.BookCountMap = bookCountMap;
            ViewBag.SwapCountMap = swapCountMap;
            ViewBag.AdminUserIds = adminUserIds;

            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (id == currentUserId)
            {
                TempData["Error"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Users));
            }

            var swaps = await _context.SwapRequests
                .Where(s => s.SenderId == id || s.ReceiverId == id)
                .ToListAsync();
            _context.SwapRequests.RemoveRange(swaps);

            var books = await _context.Books
                .Where(b => b.OwnerId == id)
                .ToListAsync();

            foreach (var book in books)
            {
                if (!string.IsNullOrEmpty(book.CoverImageUrl))
                {
                    var imgPath = Path.Combine(_environment.WebRootPath,
                        book.CoverImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(imgPath))
                        System.IO.File.Delete(imgPath);
                }
            }
            _context.Books.RemoveRange(books);

            await _context.SaveChangesAsync();
            await _userManager.DeleteAsync(user);

            TempData["Success"] = $"User {user.Email} and all their data deleted.";
            return RedirectToAction(nameof(Users));
        }

        // ── Books ──────────────────────────────────────────────────────────────

        public async Task<IActionResult> Books(string? search, string? genre, string? status)
        {
            var query = _context.Books
                .Include(b => b.Owner)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(b =>
                    EF.Functions.ILike(b.Title, $"%{search}%") ||
                    EF.Functions.ILike(b.Author, $"%{search}%"));
            }

            if (!string.IsNullOrWhiteSpace(genre))
                query = query.Where(b => b.Genre == genre);

            if (status == "available")
                query = query.Where(b => b.IsAvailable);
            else if (status == "hidden")
                query = query.Where(b => !b.IsAvailable);

            var books = await query
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Genre  = genre;
            ViewBag.Status = status;
            ViewBag.Genres = await _context.Books
                .Select(b => b.Genre)
                .Distinct()
                .OrderBy(g => g)
                .ToListAsync();

            return View(books);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBook(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();

            var swaps = await _context.SwapRequests
                .Where(s => s.RequestedBookId == id || s.OfferedBookId == id)
                .ToListAsync();
            _context.SwapRequests.RemoveRange(swaps);

            if (!string.IsNullOrEmpty(book.CoverImageUrl))
            {
                var imgPath = Path.Combine(_environment.WebRootPath,
                    book.CoverImageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(imgPath))
                    System.IO.File.Delete(imgPath);
            }

            _context.Books.Remove(book);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Book \"{book.Title}\" deleted successfully.";
            return RedirectToAction(nameof(Books));
        }

        public async Task<IActionResult> SwapRequests(string? status)
        {
            var query = _context.SwapRequests
                .Include(s => s.Sender)
                .Include(s => s.Receiver)
                .Include(s => s.RequestedBook)
                .Include(s => s.OfferedBook)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(s => s.Status == status);

            var swaps = await query
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            ViewBag.Status = status;

            return View(swaps);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSwap(int id)
        {
            var swap = await _context.SwapRequests.FindAsync(id);
            if (swap == null) return NotFound();

            _context.SwapRequests.Remove(swap);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Swap request deleted.";
            return RedirectToAction(nameof(SwapRequests));
        }
    }
}

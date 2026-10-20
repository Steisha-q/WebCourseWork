using BookSwap.Data;
using BookSwap.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BookSwap.Controllers
{
    public class BooksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public BooksController(
    ApplicationDbContext context,
    IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // Book catalog

        public async Task<IActionResult> Index(
            string? search,
            string? genre,
            string? city,
            string? condition,
            string? sort)
        {
            var query = _context.Books
    .Include(b => b.Owner)
    .AsNoTracking()
    .Where(b => b.IsAvailable)
    .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(b =>
                    EF.Functions.ILike(b.Title, $"%{search}%") ||
                    EF.Functions.ILike(b.Author, $"%{search}%"));
            }

            if (!string.IsNullOrWhiteSpace(genre))
            {
                query = query.Where(b => b.Genre == genre);
            }

            if (!string.IsNullOrWhiteSpace(city))
            {
                query = query.Where(b => b.City == city);
            }

            if (!string.IsNullOrWhiteSpace(condition))
            {
                query = query.Where(b => b.Condition == condition);
            }

            query = sort switch
            {
                "title" => query.OrderBy(b => b.Title),
                "author" => query.OrderBy(b => b.Author),
                "oldest" => query.OrderBy(b => b.CreatedAt),
                _ => query.OrderByDescending(b => b.CreatedAt)
            };

            ViewBag.Search = search;
            ViewBag.Genre = genre;
            ViewBag.City = city;
            ViewBag.Condition = condition;
            ViewBag.Sort = sort;

            ViewBag.Genres = await _context.Books
                .Where(b => b.IsAvailable)
                .Select(b => b.Genre)
                .Distinct()
                .OrderBy(g => g)
                .ToListAsync();

            ViewBag.Cities = await _context.Books
                .Where(b => b.IsAvailable && b.City != null)
                .Select(b => b.City!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return View(await query.ToListAsync());
        }

        // Book details
        public async Task<IActionResult> Details(int id)
        {
            var book = await _context.Books
    .Include(b => b.Owner)
    .FirstOrDefaultAsync(b => b.Id == id);

            if (book == null)
                return NotFound();

            return View(book);
        }

        // Add a book
        [Authorize]
        public IActionResult Create()
        {
            return View();
        }


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            Book book, IFormFile? coverImage)
        {
            ModelState.Remove(nameof(Book.OwnerId));
            ModelState.Remove(nameof(Book.CoverImageUrl));

            if (!ModelState.IsValid)
                return View(book);

            book.Id = 0;
            book.OwnerId = User.FindFirstValue(
                ClaimTypes.NameIdentifier)!;

            book.CreatedAt = DateTime.UtcNow;

            if (coverImage != null && coverImage.Length > 0)
            {
                // Maximum file size: 5 MB
                if (coverImage.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError(
                        "coverImage",
                        "Image size must not exceed 5 MB.");

                    return View(book);
                }

                // Validate image format
                var allowedExtensions = new[]
                {
            ".jpg", ".jpeg", ".png", ".webp"
        };

                var extension = Path.GetExtension(
                    coverImage.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError(
                        "coverImage",
                        "Only JPG, PNG and WEBP images are allowed.");

                    return View(book);
                }

                // Validate actual file signature

                byte[] header = new byte[12];

                using (var stream = coverImage.OpenReadStream())
                {
                    await stream.ReadExactlyAsync(header);
                }

                bool isJpeg =
                    header[0] == 0xFF &&
                    header[1] == 0xD8 &&
                    header[2] == 0xFF;

                bool isPng =
                    header.AsSpan(0, 8).SequenceEqual(
                        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

                bool isWebp =
                    System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" &&
                    System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP";

                string? actualExtension = isJpeg ? ".jpg" :
                                          isPng ? ".png" :
                                          isWebp ? ".webp" : null;

                if (actualExtension == null)
                {
                    ModelState.AddModelError(
                        "coverImage",
                        "Unsupported or invalid image file.");

                    return View(book);
                }

                // Use the detected format when saving the image
                extension = actualExtension;

                var uploadsFolder = Path.Combine(
                    _environment.WebRootPath, "images", "books");

                Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString("N")
                               + extension;

                var filePath = Path.Combine(
                    uploadsFolder, fileName);

                using (var stream = new FileStream(
                    filePath, FileMode.CreateNew))
                {
                    await coverImage.CopyToAsync(stream);
                }

                book.CoverImageUrl = "/images/books/" + fileName;
            }

            _context.Books.Add(book);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }


        // Edit a book
        [Authorize]
        public async Task<IActionResult> Edit(int id)
        {
            var book = await _context.Books.FindAsync(id);

            if (book == null)
                return NotFound();

            if (book.OwnerId != User.FindFirstValue(
                ClaimTypes.NameIdentifier))
                return Forbid();

            return View(book);
        }


        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id, Book input, IFormFile? coverImage)
        {
            var book = await _context.Books.FindAsync(id);

            if (book == null)
                return NotFound();

            if (book.OwnerId != User.FindFirstValue(
                ClaimTypes.NameIdentifier))
                return Forbid();

            ModelState.Remove(nameof(Book.OwnerId));
            ModelState.Remove(nameof(Book.CoverImageUrl));

            if (!ModelState.IsValid)
                return View(input);

            string? newCoverPath = null;

            if (coverImage != null && coverImage.Length > 0)
            {
                if (coverImage.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError(
                        "coverImage",
                        "Image size must not exceed 5 MB.");
                    return View(input);
                }

                var extension = Path.GetExtension(
                    coverImage.FileName).ToLowerInvariant();

                var allowedExtensions = new[]
                {
            ".jpg", ".jpeg", ".png", ".webp"
        };

                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError(
                        "coverImage",
                        "Only JPG, PNG and WEBP images are allowed.");
                    return View(input);
                }

                byte[] header = new byte[12];

                using (var stream = coverImage.OpenReadStream())
                {
                    if (stream.Length < header.Length)
                    {
                        ModelState.AddModelError(
                            "coverImage", "Invalid image file.");
                        return View(input);
                    }

                    await stream.ReadExactlyAsync(header);
                }

                bool isJpeg = header[0] == 0xFF &&
                              header[1] == 0xD8 &&
                              header[2] == 0xFF;

                bool isPng = header.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

                bool isWebp =
                    System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" &&
                    System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP";

                string? actualExtension = isJpeg ? ".jpg" :
                                          isPng ? ".png" :
                                          isWebp ? ".webp" : null;

                if (actualExtension == null)
                {
                    ModelState.AddModelError(
                        "coverImage",
                        "Unsupported or invalid image file.");
                    return View(input);
                }

                var uploadsFolder = Path.Combine(
                    _environment.WebRootPath, "images", "books");

                Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid().ToString("N")
                               + actualExtension;

                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(
                    filePath, FileMode.CreateNew))
                {
                    await coverImage.CopyToAsync(stream);
                }

                newCoverPath = "/images/books/" + fileName;
            }

            book.Title = input.Title;
            book.Author = input.Author;
            book.Genre = input.Genre;
            book.PublicationYear = input.PublicationYear;
            book.Publisher = input.Publisher;
            book.ISBN = input.ISBN;
            book.Pages = input.Pages;
            book.Language = input.Language;
            book.Description = input.Description;
            book.Condition = input.Condition;
            book.City = input.City;
            book.ExchangeType = input.ExchangeType;

            if (newCoverPath != null)
                book.CoverImageUrl = newCoverPath;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details),
                new { id = book.Id, from = "library" });
        }

        // Delete a book
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var book = await _context.Books.FindAsync(id);

            if (book == null)
                return NotFound();

            if (book.OwnerId != User.FindFirstValue(
                ClaimTypes.NameIdentifier))
                return Forbid();

            _context.Books.Remove(book);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        public async Task<IActionResult> MyLibrary(string? status)
        {
            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            var query = _context.Books
                .AsNoTracking()
                .Where(b => b.OwnerId == userId);

            if (status == "available")
            {
                query = query.Where(b => b.IsAvailable);
            }
            else if (status == "hidden")
            {
                query = query.Where(b => !b.IsAvailable);
            }

            ViewBag.Status = status;

            var books = await query
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();


            var bookIds = books.Select(b => b.Id).ToList();

            var lockedSwaps = await _context.SwapRequests
                .Where(s =>
                    (s.Status == "Accepted" || s.Status == "Completed") &&
                    (bookIds.Contains(s.RequestedBookId) ||
                     bookIds.Contains(s.OfferedBookId)))
                .Select(s => new
                {
                    s.RequestedBookId,
                    s.OfferedBookId,
                    s.Status
                })
                .ToListAsync();

            var bookStatuses = new Dictionary<int, string>();

            foreach (var swap in lockedSwaps)
            {
                bookStatuses[swap.RequestedBookId] = swap.Status;
                bookStatuses[swap.OfferedBookId] = swap.Status;
            }

            ViewBag.BookStatuses = bookStatuses;

            return View(books);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAvailability(int id)
        {
            var book = await _context.Books.FindAsync(id);

            if (book == null)
                return NotFound();

            var userId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (book.OwnerId != userId)
                return Forbid();


            bool isLocked = await _context.SwapRequests
                .AnyAsync(s =>
                    (s.Status == "Accepted" || s.Status == "Completed") &&
                    (s.RequestedBookId == book.Id ||
                     s.OfferedBookId == book.Id));

            if (isLocked)
            {
                TempData["Error"] =
                    "This book cannot be changed because it is part of an accepted or completed swap.";

                return RedirectToAction(nameof(MyLibrary));
            }

            book.IsAvailable = !book.IsAvailable;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(MyLibrary));
        }

    }
}

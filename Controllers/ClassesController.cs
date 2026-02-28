using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using FinalASB.Models;
using FinalASB.ViewModels;
using System.Security.Claims;

namespace FinalASB.Controllers
{
    [Authorize]
    public class ClassesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private const string ClassBlockedMessage = "Lớp học tạm thời bị khóa. Vui lòng thử lại sau!";

        public ClassesController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateClassViewModel model)
        {
            if (!ModelState.IsValid)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                    return Json(new { success = false, errors = errors.ToList() });
                }
                return View(model);
            }

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString))
            {
                ModelState.AddModelError("", "Không thể xác định người dùng. Vui lòng đăng nhập lại.");
                return View(model);
            }

            if (!int.TryParse(userIdString, out int userId))
            {
                ModelState.AddModelError("", "Thông tin người dùng không hợp lệ.");
                return View(model);
            }

            // Verify user exists and is active
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                ModelState.AddModelError("", "Người dùng không tồn tại trong hệ thống.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError("", "Tài khoản của bạn đã bị khóa.");
                return View(model);
            }

            var joinCode = GenerateJoinCode();

            // Handle image upload
            string? imageUrl = null;
            if (model.ClassImageFile != null && model.ClassImageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "class-images");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var fileExtension = Path.GetExtension(model.ClassImageFile.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("ClassImageFile", "Chỉ chấp nhận file ảnh: JPG, PNG, GIF.");
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, error = "Chỉ chấp nhận file ảnh: JPG, PNG, GIF." });
                    }
                    return View(model);
                }

                var fileName = $"{userId}_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ClassImageFile.CopyToAsync(stream);
                }

                imageUrl = $"/uploads/class-images/{fileName}";
            }

            var newClass = new Class
            {
                ClassName = model.ClassName,
                Description = model.Description,
                ClassImageUrl = imageUrl,
                IsBlock = false,
                Status = "Active",
                JoinCode = joinCode,
                OwnerId = userId,
                CreatedAt = DateTime.Now
            };

            try
            {
                _context.Classes.Add(newClass);
                await _context.SaveChangesAsync();

                // Auto-enroll the creator as Teacher
                var enrollment = new Enrollment
                {
                    UserId = userId,
                    ClassId = newClass.Id,
                    Role = "Teacher",
                    JoinedAt = DateTime.Now
                };

                _context.Enrollments.Add(enrollment);
                await _context.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    Response.ContentType = "application/json";
                    return Json(new { success = true, redirect = Url.Action("Details", new { id = newClass.Id }) });
                }
                return RedirectToAction("Details", new { id = newClass.Id });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                // Log the error for debugging
                var innerException = ex.InnerException;
                string errorMessage = "Đã xảy ra lỗi khi tạo lớp học. Vui lòng thử lại.";
                
                if (innerException != null)
                {
                    if (innerException.Message.Contains("FOREIGN KEY"))
                    {
                        errorMessage = $"Lỗi: UserId {userId} không tồn tại trong database. Vui lòng đăng nhập lại.";
                    }
                    else if (innerException.Message.Contains("PRIMARY KEY") || innerException.Message.Contains("UNIQUE"))
                    {
                        errorMessage = "Mã lớp học đã tồn tại. Vui lòng thử lại.";
                    }
                    else
                    {
                        errorMessage = $"Lỗi database: {innerException.Message}";
                    }
                }
                
                ModelState.AddModelError("", errorMessage);
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, error = errorMessage });
                }
                return View(model);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Đã xảy ra lỗi: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" - {ex.InnerException.Message}";
                }
                ModelState.AddModelError("", errorMessage);
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, error = errorMessage });
                }
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateImage(int id, IFormFile? ClassImageFile)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var classEntity = await _context.Classes.FindAsync(id);
            
            if (classEntity == null)
            {
                return NotFound();
            }

            if (classEntity.OwnerId != userId)
            {
                return Forbid();
            }

            if (ClassImageFile != null && ClassImageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "class-images");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var fileExtension = Path.GetExtension(ClassImageFile.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    TempData["ErrorMessage"] = "Chỉ chấp nhận file ảnh: JPG, PNG, GIF.";
                    return RedirectToAction("Details", new { id });
                }

                // Delete old image if exists
                if (!string.IsNullOrEmpty(classEntity.ClassImageUrl) && classEntity.ClassImageUrl.StartsWith("/uploads/class-images/"))
                {
                    var oldImagePath = Path.Combine(_environment.WebRootPath, classEntity.ClassImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }

                var fileName = $"{id}_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ClassImageFile.CopyToAsync(stream);
                }

                classEntity.ClassImageUrl = $"/uploads/class-images/{fileName}";
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Đã cập nhật ảnh lớp học thành công!";
            }
            else
            {
                // Delete image if no file uploaded
                if (!string.IsNullOrEmpty(classEntity.ClassImageUrl) && classEntity.ClassImageUrl.StartsWith("/uploads/class-images/"))
                {
                    var oldImagePath = Path.Combine(_environment.WebRootPath, classEntity.ClassImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }
                classEntity.ClassImageUrl = null;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa ảnh lớp học!";
            }

            return RedirectToAction("Details", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var enrollment = await _context.Enrollments
                .Include(e => e.Class)
                    .ThenInclude(c => c.Owner)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Enrollments)
                        .ThenInclude(en => en.User)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Assignments)
                        .ThenInclude(a => a.Creator)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Assignments)
                        .ThenInclude(a => a.Submissions)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Assignments)
                        .ThenInclude(a => a.Attachments)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Announcements)
                        .ThenInclude(an => an.User)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Announcements)
                        .ThenInclude(an => an.Attachments)
                .Include(e => e.Class)
                    .ThenInclude(c => c.Comments)
                .FirstOrDefaultAsync(e => e.ClassId == id && e.UserId == userId);

            if (enrollment == null)
            {
                return NotFound();
            }

            if (enrollment.Class.IsBlock)
            {
                TempData["ErrorMessage"] = ClassBlockedMessage;
                return RedirectToAction("Index", "Home");
            }

            ViewBag.UserRole = enrollment.Role;
            ViewBag.IsOwner = enrollment.Class.OwnerId == userId;

            return View(enrollment.Class);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string ClassName, string? Description)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var classEntity = await _context.Classes.FindAsync(id);
            
            if (classEntity == null)
            {
                return Json(new { success = false, error = "Lớp học không tồn tại." });
            }

            if (classEntity.OwnerId != userId)
            {
                return Json(new { success = false, error = "Bạn không có quyền chỉnh sửa lớp học này." });
            }

            if (string.IsNullOrWhiteSpace(ClassName))
            {
                return Json(new { success = false, error = "Tên lớp học không được để trống." });
            }

            classEntity.ClassName = ClassName;
            classEntity.Description = Description;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Đã cập nhật thông tin lớp học thành công!" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var classEntity = await _context.Classes.FindAsync(id);
            
            if (classEntity == null)
            {
                return Json(new { success = false, error = "Lớp học không tồn tại." });
            }

            if (classEntity.OwnerId != userId)
            {
                return Json(new { success = false, error = "Bạn không có quyền xóa lớp học này." });
            }

            classEntity.Status = "Non-active";
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Đã xóa lớp học thành công!" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.ClassId == id && e.UserId == userId);
            
            if (enrollment == null)
            {
                return Json(new { success = false, error = "Bạn chưa tham gia lớp học này." });
            }

            if (enrollment.Role == "Teacher")
            {
                return Json(new { success = false, error = "Giáo viên không thể rời lớp học. Vui lòng xóa lớp học thay vì rời khỏi." });
            }

            _context.Enrollments.Remove(enrollment);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Đã rời lớp học thành công!" });
        }

        [HttpGet]
        public IActionResult Join()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                ModelState.AddModelError("", "Mã lớp học không được để trống.");
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, error = "Mã lớp học không được để trống." });
                }
                return View();
            }

            var classEntity = await _context.Classes
                .FirstOrDefaultAsync(c => c.JoinCode == joinCode);

            if (classEntity == null)
            {
                ModelState.AddModelError("", "Mã lớp học không hợp lệ.");
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, error = "Mã lớp học không hợp lệ." });
                }
                return View();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Check if already enrolled
            var existingEnrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == classEntity.Id);

            if (existingEnrollment != null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, error = "Bạn đã có trong lớp này" });
                }
                ModelState.AddModelError("", "Bạn đã có trong lớp này");
                return View();
            }

            var enrollment = new Enrollment
            {
                UserId = userId,
                ClassId = classEntity.Id,
                Role = "Student",
                JoinedAt = DateTime.Now
            };

            try
            {
                _context.Enrollments.Add(enrollment);
                await _context.SaveChangesAsync();
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, message = "Tham gia lớp học thành công!" });
                }
                TempData["SuccessMessage"] = "Tham gia lớp học thành công!";
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                string errorMessage = "Đã xảy ra lỗi khi tham gia lớp học. Vui lòng thử lại.";
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, error = errorMessage });
                }
                ModelState.AddModelError("", errorMessage);
                return View();
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, redirect = Url.Action("Details", new { id = classEntity.Id }) });
            }
            return RedirectToAction("Details", new { id = classEntity.Id });
        }

        private string GenerateJoinCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            string code;
            bool isUnique;

            do
            {
                code = new string(Enumerable.Repeat(chars, 6)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
                isUnique = !_context.Classes.Any(c => c.JoinCode == code);
            } while (!isUnique);

            return code;
        }
    }
}


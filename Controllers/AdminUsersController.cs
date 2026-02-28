using FinalASB.Data;
using FinalASB.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using BCrypt.Net;
using FinalASB.ViewModels;

namespace FinalASB.Controllers
{
    public class AdminUsersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminUsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string search, bool? isActive)
        {
            var users = _context.Users
                .Where(u => u.SystemRoleId == 2)
                .Include(u => u.Enrollments)
                    .ThenInclude(e => e.Class)
                .Include(u => u.OwnedClasses)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                users = users.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));

            if (isActive.HasValue)
                users = users.Where(u => u.IsActive == isActive.Value);

            var result = await users
                .Select(u => new UserViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,

                    // ⭐ Chỉ đếm lớp mà user đã đăng ký vào & lớp đó đang Active
                    EnrolledCount = u.Enrollments
                        .Count(e => e.Class.Status == "Active" && e.Class.OwnerId != u.Id),

                    // ⭐ Chỉ đếm các lớp user đó tạo và đang Active
                    OwnedClasses = u.OwnedClasses
                        .Count(c => c.Status == "Active")
                })
                .ToListAsync();

            return View(result);
        }

        // Tạo người dùng mới
        [ResponseCache(NoStore = true, Duration = 0)]
        public IActionResult Create()
        {
            ViewBag.Roles = new SelectList(_context.SystemRoles.ToList(), "Id", "RoleName");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User user, string Password, string ConfirmPassword, string AdminPassword)
        {
            // Xóa lỗi ModelState cho SystemRole navigation property (nếu có)
            ModelState.Remove("SystemRole");

            // Kiểm tra SystemRoleId
            if (user.SystemRoleId <= 0)
            {
                ModelState.AddModelError("SystemRoleId", "Vui lòng chọn vai trò.");
                user.SystemRoleId = 2; // Set default để tránh lỗi khi return view
            }
            else
            {
                // Kiểm tra SystemRoleId có tồn tại trong database không
                var roleExists = await _context.SystemRoles.AnyAsync(r => r.Id == user.SystemRoleId);
                if (!roleExists)
                {
                    ModelState.AddModelError("SystemRoleId", "Vai trò không hợp lệ.");
                    user.SystemRoleId = 2; // Set default
                }
            }

            // Kiểm tra mật khẩu admin
            if (string.IsNullOrEmpty(AdminPassword))
            {
                ModelState.AddModelError("AdminPassword", "Vui lòng nhập mật khẩu admin để xác nhận.");
            }
            else
            {
                // Lấy thông tin admin hiện tại
                var adminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
                var admin = await _context.Users.FindAsync(adminId);

                if (admin == null || admin.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(AdminPassword, admin.PasswordHash))
                {
                    ModelState.AddModelError("AdminPassword", "Mật khẩu admin không đúng.");
                }
            }

            // Kiểm tra email đã tồn tại chưa
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
            {
                ModelState.AddModelError("Email", "Email này đã được sử dụng.");
            }

            // Kiểm tra password
            if (string.IsNullOrEmpty(Password))
            {
                ModelState.AddModelError("Password", "Mật khẩu là bắt buộc.");
            }
            else if (Password.Length < 6)
            {
                ModelState.AddModelError("Password", "Mật khẩu phải có ít nhất 6 ký tự.");
            }

            if (Password != ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Mật khẩu xác nhận không khớp.");
            }

            if (ModelState.IsValid)
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
                user.IsActive = true;
                user.CreatedAt = DateTime.Now;

                // Đảm bảo SystemRoleId có giá trị hợp lệ
                if (user.SystemRoleId <= 0)
                {
                    user.SystemRoleId = 2; // Default to User role
                }

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                TempData["AdminSuccessMessage"] = $"Tạo tài khoản thành công cho {user.FullName}";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Roles = new SelectList(_context.SystemRoles.ToList(), "Id", "RoleName", user.SystemRoleId);
            return View(user);
        }

        // Vô hiệu hóa / kích hoạt người dùng
        public async Task<IActionResult> ToggleActive(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // API: Kiểm tra email đã tồn tại chưa
        [HttpGet]
        public async Task<IActionResult> CheckEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return Json(new { exists = false });
            }

            var exists = await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
            return Json(new { exists });
        }

        // API: Kiểm tra tên người dùng đã tồn tại chưa
        [HttpGet]
        public async Task<IActionResult> CheckFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return Json(new { exists = false });
            }

            var exists = await _context.Users.AnyAsync(u => u.FullName.ToLower() == fullName.ToLower());
            return Json(new { exists });
        }

        // Chỉnh sửa người dùng
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string FullName, string Email, string OldPassword, string Password, string ConfirmPassword, string AdminPassword)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            // Xóa các lỗi ModelState không hợp lệ từ client-side validation cho các trường không bắt buộc
            ModelState.Remove("OldPassword");
            ModelState.Remove("Password");
            ModelState.Remove("ConfirmPassword");

            // Kiểm tra mật khẩu admin
            if (string.IsNullOrEmpty(AdminPassword))
            {
                ModelState.AddModelError("AdminPassword", "Vui lòng nhập mật khẩu admin để xác nhận.");
            }
            else
            {
                var adminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
                var admin = await _context.Users.FindAsync(adminId);

                if (admin == null || admin.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(AdminPassword, admin.PasswordHash))
                {
                    ModelState.AddModelError("AdminPassword", "Mật khẩu admin không đúng.");
                }
            }

            // Kiểm tra email đã tồn tại chưa (trừ chính user đang sửa)
            if (!string.IsNullOrEmpty(Email) && Email != user.Email)
            {
                if (await _context.Users.AnyAsync(u => u.Email == Email && u.Id != id))
                {
                    ModelState.AddModelError("Email", "Email này đã được sử dụng.");
                }
            }

            // Kiểm tra FullName
            if (string.IsNullOrEmpty(FullName))
            {
                ModelState.AddModelError("FullName", "Tên người dùng là bắt buộc.");
            }

            // Kiểm tra Email
            if (string.IsNullOrEmpty(Email))
            {
                ModelState.AddModelError("Email", "Email là bắt buộc.");
            }

            // Kiểm tra mật khẩu nếu có nhập mật khẩu mới
            // Admin có thể đổi mật khẩu mà không cần mật khẩu cũ (đã có mật khẩu admin xác nhận)
            bool hasNewPassword = !string.IsNullOrEmpty(Password);
            bool hasConfirmPassword = !string.IsNullOrEmpty(ConfirmPassword);

            if (hasNewPassword)
            {
                // Kiểm tra độ dài mật khẩu mới
                if (Password.Length < 6)
                {
                    ModelState.AddModelError("Password", "Mật khẩu phải có ít nhất 6 ký tự.");
                }

                // Kiểm tra xác nhận mật khẩu
                if (string.IsNullOrEmpty(ConfirmPassword))
                {
                    ModelState.AddModelError("ConfirmPassword", "Vui lòng xác nhận mật khẩu mới.");
                }
                else if (Password != ConfirmPassword)
                {
                    ModelState.AddModelError("ConfirmPassword", "Mật khẩu xác nhận không khớp.");
                }
            }
            else if (hasConfirmPassword)
            {
                // Nếu có nhập xác nhận mật khẩu nhưng không có mật khẩu mới
                ModelState.AddModelError("Password", "Vui lòng nhập mật khẩu mới.");
            }

            if (ModelState.IsValid)
            {
                // Cập nhật thông tin
                user.FullName = FullName;
                user.Email = Email;

                // Cập nhật mật khẩu nếu có nhập
                if (hasNewPassword)
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
                }

                await _context.SaveChangesAsync();
                TempData["AdminSuccessMessage"] = $"Cập nhật tài khoản thành công cho {user.FullName}";
                return RedirectToAction(nameof(Index));
            }

            return View(user);
        }

        // Xóa nhiều người dùng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMultiple(string userIds, string AdminPassword)
        {
            if (string.IsNullOrEmpty(userIds))
            {
                TempData["AdminErrorMessage"] = "Vui lòng chọn ít nhất một người dùng để xóa.";
                return RedirectToAction(nameof(Index));
            }

            var userIdArray = userIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id, out var result) ? result : 0)
                .Where(id => id > 0)
                .ToArray();

            if (userIdArray.Length == 0)
            {
                TempData["AdminErrorMessage"] = "Vui lòng chọn ít nhất một người dùng để xóa.";
                return RedirectToAction(nameof(Index));
            }

            // Kiểm tra mật khẩu admin
            if (string.IsNullOrEmpty(AdminPassword))
            {
                TempData["AdminErrorMessage"] = "Vui lòng nhập mật khẩu admin để xác nhận.";
                return RedirectToAction(nameof(Index));
            }

            var adminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            var admin = await _context.Users.FindAsync(adminId);

            if (admin == null || admin.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(AdminPassword, admin.PasswordHash))
            {
                TempData["AdminErrorMessage"] = "Mật khẩu admin không đúng.";
                return RedirectToAction(nameof(Index));
            }

            // Không cho phép xóa chính admin
            if (userIdArray.Contains(adminId))
            {
                TempData["AdminErrorMessage"] = "Không thể xóa tài khoản admin hiện tại.";
                return RedirectToAction(nameof(Index));
            }

            var users = await _context.Users
                .Where(u => userIdArray.Contains(u.Id))
                .ToListAsync();

            if (users.Any())
            {
                var userNames = string.Join(", ", users.Select(u => u.FullName));
                var userIdsToDelete = users.Select(u => u.Id).ToList();

                // Kiểm tra xem có user nào sở hữu lớp học không
                var usersWithOwnedClasses = await _context.Classes
                    .Where(c => userIdsToDelete.Contains(c.OwnerId))
                    .Select(c => c.OwnerId)
                    .Distinct()
                    .ToListAsync();

                if (usersWithOwnedClasses.Any())
                {
                    var usersWithClasses = users.Where(u => usersWithOwnedClasses.Contains(u.Id))
                        .Select(u => u.FullName)
                        .ToList();
                    TempData["AdminErrorMessage"] = $"Không thể xóa các người dùng sau vì họ đang sở hữu lớp học: {string.Join(", ", usersWithClasses)}.";
                    return RedirectToAction(nameof(Index));
                }

                // Xóa tất cả các bản ghi liên quan trước khi xóa User
                // 1. Xóa Comments (chỉ xóa Comments mà user này tạo, TargetUserId sẽ được set null tự động)
                var commentsToDelete = await _context.Comments
                    .Where(c => userIdsToDelete.Contains(c.UserId))
                    .ToListAsync();
                if (commentsToDelete.Any())
                {
                    _context.Comments.RemoveRange(commentsToDelete);
                }

                // Set null cho TargetUserId trong Comments (nếu có)
                var commentsWithTargetUser = await _context.Comments
                    .Where(c => c.TargetUserId.HasValue && userIdsToDelete.Contains(c.TargetUserId.Value))
                    .ToListAsync();
                if (commentsWithTargetUser.Any())
                {
                    foreach (var comment in commentsWithTargetUser)
                    {
                        comment.TargetUserId = null;
                    }
                }

                // 2. Xóa Announcements
                var announcementsToDelete = await _context.Announcements
                    .Where(a => userIdsToDelete.Contains(a.UserId))
                    .ToListAsync();
                if (announcementsToDelete.Any())
                {
                    _context.Announcements.RemoveRange(announcementsToDelete);
                }

                // 3. Xóa Submissions
                var submissionsToDelete = await _context.Submissions
                    .Where(s => userIdsToDelete.Contains(s.StudentId))
                    .ToListAsync();
                if (submissionsToDelete.Any())
                {
                    _context.Submissions.RemoveRange(submissionsToDelete);
                }

                // 4. Xóa Assignments
                var assignmentsToDelete = await _context.Assignments
                    .Where(a => userIdsToDelete.Contains(a.CreatedBy))
                    .ToListAsync();
                if (assignmentsToDelete.Any())
                {
                    _context.Assignments.RemoveRange(assignmentsToDelete);
                }

                // 5. Xóa Enrollments
                var enrollmentsToDelete = await _context.Enrollments
                    .Where(e => userIdsToDelete.Contains(e.UserId))
                    .ToListAsync();
                if (enrollmentsToDelete.Any())
                {
                    _context.Enrollments.RemoveRange(enrollmentsToDelete);
                }

                // 6. Xóa Users
                _context.Users.RemoveRange(users);

                await _context.SaveChangesAsync();
                TempData["AdminSuccessMessage"] = $"Đã xóa {users.Count} người dùng: {userNames}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}

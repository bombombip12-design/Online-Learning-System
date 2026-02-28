using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using FinalASB.Models;
using FinalASB.ViewModels;
using System.Security.Claims;
using BCrypt.Net;

namespace FinalASB.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public ProfileController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users
                .Include(u => u.SystemRole)
                .FirstOrDefaultAsync(u => u.Id == userId);
            
            if (user == null)
            {
                return NotFound();
            }

            var viewModel = new ProfileViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                IsEmailVerified = !string.IsNullOrEmpty(user.GoogleId), // Giả sử có GoogleId là đã xác thực
                CreatedAt = user.CreatedAt,
                GoogleId = user.GoogleId
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(UpdateProfileViewModel model)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users
                .Include(u => u.SystemRole)
                .FirstOrDefaultAsync(u => u.Id == userId);
            
            if (user == null)
            {
                return NotFound();
            }

            // Validate password if provided
            if (!string.IsNullOrEmpty(model.NewPassword))
            {
                // Check if old password is provided
                if (string.IsNullOrEmpty(model.OldPassword))
                {
                    ModelState.AddModelError("OldPassword", "Vui lòng nhập mật khẩu cũ.");
                }
                else
                {
                    // Verify old password
                    if (user.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(model.OldPassword, user.PasswordHash))
                    {
                        ModelState.AddModelError("OldPassword", "Mật khẩu cũ không đúng.");
                    }
                }

                if (model.NewPassword.Length < 6)
                {
                    ModelState.AddModelError("NewPassword", "Mật khẩu phải có ít nhất 6 ký tự.");
                }
                else if (model.NewPassword != model.ConfirmPassword)
                {
                    ModelState.AddModelError("ConfirmPassword", "Mật khẩu xác nhận không khớp.");
                }
            }

            if (!ModelState.IsValid)
            {
                var profileViewModel = new ProfileViewModel
                {
                    Id = user.Id,
                    FullName = model.FullName,
                    Email = user.Email,
                    AvatarUrl = user.AvatarUrl,
                    IsEmailVerified = !string.IsNullOrEmpty(user.GoogleId),
                    CreatedAt = user.CreatedAt,
                    GoogleId = user.GoogleId,
                    OldPassword = model.OldPassword,
                    NewPassword = model.NewPassword,
                    ConfirmPassword = model.ConfirmPassword
                };
                return View("Index", profileViewModel);
            }

            // Update FullName
            if (!string.IsNullOrEmpty(model.FullName))
            {
                user.FullName = model.FullName;
            }

            // Update Password
            if (!string.IsNullOrEmpty(model.NewPassword))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            }

            // Handle Avatar Upload
            if (model.AvatarFile != null && model.AvatarFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "avatars");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var fileExtension = Path.GetExtension(model.AvatarFile.FileName).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("AvatarFile", "Chỉ chấp nhận file ảnh (jpg, jpeg, png, gif).");
                    var profileViewModel = new ProfileViewModel
                    {
                        Id = user.Id,
                        FullName = model.FullName,
                        Email = user.Email,
                        AvatarUrl = user.AvatarUrl,
                        IsEmailVerified = !string.IsNullOrEmpty(user.GoogleId),
                        CreatedAt = user.CreatedAt,
                        GoogleId = user.GoogleId
                    };
                    return View("Index", profileViewModel);
                }

                // Delete old avatar if exists
                if (!string.IsNullOrEmpty(user.AvatarUrl) && user.AvatarUrl.StartsWith("/uploads/avatars/"))
                {
                    var oldAvatarPath = Path.Combine(_environment.WebRootPath, user.AvatarUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldAvatarPath))
                    {
                        System.IO.File.Delete(oldAvatarPath);
                    }
                }

                // Generate unique filename
                var fileName = $"{userId}_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.AvatarFile.CopyToAsync(stream);
                }

                user.AvatarUrl = $"/uploads/avatars/{fileName}";
            }

            await _context.SaveChangesAsync();

            // Update claims if name changed
            if (!string.IsNullOrEmpty(model.FullName))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.FullName),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Role, user.SystemRole?.RoleName ?? "User")
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity));
            }

            TempData["SuccessMessage"] = "Cập nhật hồ sơ thành công!";
            TempData["AvatarUpdated"] = "true"; // Flag để JavaScript biết cần reload avatar
            return RedirectToAction("Index");
        }
    }
}


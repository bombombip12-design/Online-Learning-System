using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using FinalASB.Models;
using System.IO;
using System.Security.Claims;
using System.Text.Json;

namespace FinalASB.Controllers
{
    [Authorize]
    public class AnnouncementsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const string ClassBlockedMessage = "Lớp học tạm thời bị khóa. Vui lòng thử lại sau!";
        private readonly IWebHostEnvironment _environment;

        public AnnouncementsController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int classId, string content, string? attachmentsJson, List<IFormFile>? attachedFiles)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                TempData["ErrorMessage"] = "Nội dung thông báo không được để trống.";
                return RedirectToAction("Details", "Classes", new { id = classId });
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == classId);

            if (enrollment == null)
            {
                return Forbid();
            }

            var classEntity = await _context.Classes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == classId);

            if (classEntity == null)
            {
                return NotFound();
            }

            if (classEntity.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            var announcement = new Announcement
            {
                ClassId = classId,
                UserId = userId,
                Content = content,
                CreatedAt = DateTime.Now
            };

            _context.Announcements.Add(announcement);
            await _context.SaveChangesAsync();

            // Save link/youtube attachments if any
            if (!string.IsNullOrWhiteSpace(attachmentsJson))
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var attachments = System.Text.Json.JsonSerializer.Deserialize<List<AttachmentDto>>(attachmentsJson, options);
                    if (attachments != null && attachments.Any())
                    {
                        foreach (var att in attachments)
                        {
                            var attachment = new AnnouncementAttachment
                            {
                                AnnouncementId = announcement.Id,
                                Type = att.Type,
                                Title = att.Title,
                                Url = att.Url ?? string.Empty,
                                VideoId = att.VideoId,
                                FileName = att.FileName
                            };
                            _context.AnnouncementAttachments.Add(attachment);
                        }
                    }
                }
                catch
                {
                    // Ignore JSON parsing errors
                }
            }

            // Save uploaded files if any
            if (attachedFiles != null && attachedFiles.Any())
            {
                var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "announcement-files", announcement.Id.ToString());
                if (!Directory.Exists(uploadRoot))
                {
                    Directory.CreateDirectory(uploadRoot);
                }

                foreach (var file in attachedFiles)
                {
                    if (file == null || file.Length == 0)
                    {
                        continue;
                    }

                    var originalName = Path.GetFileName(file.FileName);
                    var safeName = Path.GetFileNameWithoutExtension(originalName);
                    var extension = Path.GetExtension(originalName);
                    var uniqueName = $"{safeName}_{Guid.NewGuid():N}{extension}";
                    var filePath = Path.Combine(uploadRoot, uniqueName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var relativeUrl = $"/uploads/announcement-files/{announcement.Id}/{uniqueName}";

                    var attachment = new AnnouncementAttachment
                    {
                        AnnouncementId = announcement.Id,
                        Type = "File",
                        Title = originalName,
                        FileName = originalName,
                        Url = relativeUrl
                    };

                    _context.AnnouncementAttachments.Add(attachment);
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Classes", new { id = classId });
        }

        [HttpGet]
        public async Task<IActionResult> Get(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var announcement = await _context.Announcements
                .Include(a => a.Attachments)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (announcement == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == announcement.ClassId && e.Role == "Teacher");

            if (enrollment == null)
            {
                return Forbid();
            }

            var payload = new
            {
                id = announcement.Id,
                content = announcement.Content,
                attachments = announcement.Attachments.Select(a => new
                {
                    id = a.Id,
                    type = a.Type,
                    title = a.Title,
                    url = a.Url,
                    fileName = a.FileName,
                    videoId = a.VideoId
                })
            };

            return Json(new { success = true, data = payload });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, int classId, string content, string? attachmentsJson, string? removedAttachmentIds, List<IFormFile>? attachedFiles)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                TempData["ErrorMessage"] = "Nội dung thông báo không được để trống.";
                return RedirectToAction("Details", "Classes", new { id = classId });
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var announcement = await _context.Announcements
                .Include(a => a.Attachments)
                .FirstOrDefaultAsync(a => a.Id == id && a.ClassId == classId);

            if (announcement == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == classId && e.Role == "Teacher");

            if (enrollment == null)
            {
                return Forbid();
            }

            if (announcement.Class?.IsBlock == true)
            {
                return RedirectWithClassBlockedMessage();
            }

            announcement.Content = content;

            // remove marked attachments
            if (!string.IsNullOrWhiteSpace(removedAttachmentIds))
            {
                try
                {
                    var ids = JsonSerializer.Deserialize<List<int>>(removedAttachmentIds, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<int>();
                    if (ids.Any())
                    {
                        var toRemove = announcement.Attachments.Where(a => ids.Contains(a.Id)).ToList();
                        foreach (var attachment in toRemove)
                        {
                            await RemoveAttachmentAsync(attachment);
                        }
                    }
                }
                catch
                {
                    // ignore parse errors
                }
            }

            // new link/youtube attachments
            if (!string.IsNullOrWhiteSpace(attachmentsJson))
            {
                try
                {
                    var attachments = JsonSerializer.Deserialize<List<AttachmentDto>>(attachmentsJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (attachments != null && attachments.Any())
                    {
                        foreach (var att in attachments)
                        {
                            var attachment = new AnnouncementAttachment
                            {
                                AnnouncementId = announcement.Id,
                                Type = att.Type,
                                Title = att.Title,
                                Url = att.Url ?? string.Empty,
                                VideoId = att.VideoId,
                                FileName = att.FileName
                            };
                            _context.AnnouncementAttachments.Add(attachment);
                        }
                    }
                }
                catch
                {
                    // ignore parsing errors
                }
            }

            if (attachedFiles != null && attachedFiles.Any())
            {
                await SaveFileAttachmentsAsync(announcement.Id, attachedFiles);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Classes", new { id = classId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var announcement = await _context.Announcements
                .Include(a => a.Class)
                .Include(a => a.Attachments)
                .Include(a => a.Comments)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (announcement == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == announcement.ClassId);

            if (enrollment == null || (enrollment.Role != "Teacher" && announcement.UserId != userId))
            {
                return Forbid();
            }

            if (announcement.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            var classId = announcement.ClassId;

            if (announcement.Comments != null && announcement.Comments.Any())
            {
                _context.Comments.RemoveRange(announcement.Comments.ToList());
            }

            if (announcement.Attachments.Any())
            {
                foreach (var attachment in announcement.Attachments.ToList())
                {
                    await RemoveAttachmentAsync(attachment);
                }
            }

            _context.Announcements.Remove(announcement);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Classes", new { id = classId });
        }

        private async Task SaveFileAttachmentsAsync(int announcementId, IEnumerable<IFormFile> files)
        {
            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "announcement-files", announcementId.ToString());
            if (!Directory.Exists(uploadRoot))
            {
                Directory.CreateDirectory(uploadRoot);
            }

            foreach (var file in files)
            {
                if (file == null || file.Length == 0)
                {
                    continue;
                }

                var originalName = Path.GetFileName(file.FileName);
                var extension = Path.GetExtension(originalName);
                var safeName = $"{Guid.NewGuid():N}{extension}";
                var filePath = Path.Combine(uploadRoot, safeName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var relativeUrl = $"/uploads/announcement-files/{announcementId}/{safeName}";

                var attachment = new AnnouncementAttachment
                {
                    AnnouncementId = announcementId,
                    Type = "File",
                    Title = originalName,
                    FileName = originalName,
                    Url = relativeUrl
                };

                _context.AnnouncementAttachments.Add(attachment);
            }
        }

        private async Task RemoveAttachmentAsync(AnnouncementAttachment attachment)
        {
            if (attachment.Type == "File" && !string.IsNullOrWhiteSpace(attachment.Url))
            {
                var physicalPath = Path.Combine(_environment.WebRootPath, attachment.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(physicalPath))
                {
                    try
                    {
                        System.IO.File.Delete(physicalPath);
                    }
                    catch
                    {
                        // ignore errors
                    }
                }
            }

            _context.AnnouncementAttachments.Remove(attachment);
            await Task.CompletedTask;
        }

        private class AttachmentDto
        {
            public string Type { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? Url { get; set; }
            public string? VideoId { get; set; }
            public string? FileName { get; set; }
        }
        private IActionResult RedirectWithClassBlockedMessage()
        {
            TempData["ErrorMessage"] = ClassBlockedMessage;
            return RedirectToAction("Index", "Home");
        }
    }
}


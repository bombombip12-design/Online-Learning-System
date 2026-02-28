        

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using FinalASB.Models;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.IO;

namespace FinalASB.Controllers
{
    [Authorize]
    public class AssignmentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const string ClassBlockedMessage = "Lớp học tạm thời bị khóa. Vui lòng thử lại sau!";
        private readonly IWebHostEnvironment _environment;

        public AssignmentsController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Create(int classId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            
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

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == classId);

            if (enrollment == null || enrollment.Role != "Teacher")
            {
                return Forbid();
            }

            ViewBag.ClassId = classId;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Assignment assignment, int? classId, string? attachmentsJson, List<IFormFile>? attachedFiles)
        {
            try
            {
                var resolvedClassId = assignment.ClassId != 0
                    ? assignment.ClassId
                    : (classId ?? 0);
                if (resolvedClassId == 0)
                {
                    TempData["ErrorMessage"] = "Không thể xác định lớp học. Vui lòng thử lại.";
                    return RedirectToAction("Index", "Home");
                }
                assignment.ClassId = resolvedClassId;
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

                if (string.IsNullOrWhiteSpace(assignment.Title))
                {
                    return RedirectToClassDetailsWithMessage(assignment.ClassId, errorMessage: "Tiêu đề bài tập là bắt buộc.");
                }

                var now = DateTime.Now;
                var effectivePublishAt = assignment.PublishAt ?? now;

                if (assignment.PublishAt.HasValue && assignment.PublishAt.Value < now.AddMinutes(-1))
                {
                    return RedirectToClassDetailsWithMessage(assignment.ClassId, errorMessage: "Ngày đăng bài không được trước thời điểm hiện tại.");
                }

                if (assignment.DueDate.HasValue && assignment.DueDate.Value <= effectivePublishAt)
                {
                    return RedirectToClassDetailsWithMessage(assignment.ClassId, errorMessage: "Hạn nộp bài phải lớn hơn thời điểm đăng bài.");
                }

                var classEntity = await _context.Classes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == assignment.ClassId);

                if (classEntity == null)
                {
                    return RedirectToClassDetailsWithMessage(assignment.ClassId, errorMessage: "Lớp học không tồn tại.");
                }

                if (classEntity.IsBlock)
                {
                    return RedirectWithClassBlockedMessage();
                }

                var enrollment = await _context.Enrollments
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

                if (enrollment == null || enrollment.Role != "Teacher")
                {
                    return RedirectToClassDetailsWithMessage(assignment.ClassId, errorMessage: "Bạn không có quyền tạo bài tập cho lớp học này.");
                }

                var newAssignment = new Assignment
                {
                    ClassId = assignment.ClassId,
                    Title = assignment.Title?.Trim(),
                    Description = assignment.Description?.Trim(),
                    DueDate = assignment.DueDate,
                    PublishAt = effectivePublishAt,
                    CreatedBy = userId,
                    CreatedAt = now
                };

                _context.Assignments.Add(newAssignment);
                await _context.SaveChangesAsync();

                await SaveAttachmentsAsync(newAssignment.Id, attachmentsJson, attachedFiles);
                await _context.SaveChangesAsync();

                return RedirectToClassDetailsWithMessage(newAssignment.ClassId, successMessage: "Bài tập đã được tạo thành công!");
            }
            catch (Exception ex)
            {
                // Log error
                var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AssignmentsController>>();
                logger.LogError(ex, "Error creating assignment: {Message}", ex.Message);

                TempData["ErrorMessage"] = "Đã xảy ra lỗi khi tạo bài tập. Vui lòng thử lại.";
                if (assignment.ClassId > 0)
                {
                    return RedirectToAction("Details", "Classes", new { id = assignment.ClassId });
                }
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Class)
                .Include(a => a.Creator)
                .Include(a => a.Attachments)
                .Include(a => a.Submissions)
                    .ThenInclude(s => s.Student)
                .Include(a => a.Submissions)
                    .ThenInclude(s => s.Attachments)
                .Include(a => a.Comments)
                    .ThenInclude(c => c.User)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (assignment == null)
            {
                return NotFound();
            }

            // Check if user is enrolled in the class
            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

            if (enrollment == null)
            {
                return Forbid();
            }

            if (assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            if (assignment.PublishAt.HasValue && assignment.PublishAt.Value > DateTime.Now && enrollment.Role != "Teacher")
            {
                TempData["ErrorMessage"] = "Bài tập này sẽ được đăng vào " + assignment.PublishAt.Value.ToString("dd/MM/yyyy HH:mm") + ".";
                return RedirectToAction("Details", "Classes", new { id = assignment.ClassId });
            }

            // Load all submissions with their files and comments for teacher view
            var allSubmissions = await _context.Submissions
                .Include(s => s.Student)
                .Include(s => s.Attachments)
                .Include(s => s.Comments)
                    .ThenInclude(c => c.User)
                .Where(s => s.AssignmentId == id)
                .OrderBy(s => s.Student.FullName)
                .ToListAsync();

            // Lấy danh sách học sinh trong lớp
            var classStudents = await _context.Enrollments
                .Include(e => e.User)
                .Where(e => e.ClassId == assignment.ClassId && e.Role == "Student")
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            // Avatar của user hiện tại (dùng cho form nhập nhận xét)
            var currentUser = await _context.Users.FindAsync(userId);

            ViewBag.UserRole = enrollment.Role;
            ViewBag.UserId = userId;
            ViewBag.CurrentUserAvatarUrl = currentUser?.AvatarUrl;
            ViewBag.UserSubmission = await _context.Submissions
                .Include(s => s.Attachments)
                .Include(s => s.Comments)
                    .ThenInclude(c => c.User)
                .FirstOrDefaultAsync(s => s.AssignmentId == id && s.StudentId == userId);
            ViewBag.AllSubmissions = allSubmissions;
            ViewBag.ClassStudents = classStudents;

            return View(assignment);
        }

        [HttpGet]
        public async Task<IActionResult> Get(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Attachments)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (assignment == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId && e.Role == "Teacher");

            if (enrollment == null)
            {
                return Forbid();
            }

            var payload = new
            {
                id = assignment.Id,
                classId = assignment.ClassId,
                title = assignment.Title,
                description = assignment.Description,
                publishAt = assignment.PublishAt?.ToString("yyyy-MM-ddTHH:mm"),
                dueDate = assignment.DueDate?.ToString("yyyy-MM-ddTHH:mm"),
                attachments = assignment.Attachments.Select(att => new
                {
                    id = att.Id,
                    type = att.Type,
                    title = att.Title,
                    url = att.Url,
                    fileName = att.FileName,
                    videoId = att.VideoId
                })
            };

            return Json(new { success = true, data = payload });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Class)
                .Include(a => a.Attachments)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (assignment == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

            if (enrollment == null || enrollment.Role != "Teacher")
            {
                return Forbid();
            }

            if (assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            return View(assignment);
        }

        private async Task SaveAttachmentsAsync(int assignmentId, string? attachmentsJson, List<IFormFile>? attachedFiles)
        {
            if (!string.IsNullOrWhiteSpace(attachmentsJson))
            {
                try
                {
                    var newAttachments = JsonSerializer.Deserialize<List<AttachmentDto>>(attachmentsJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (newAttachments != null)
                    {
                        foreach (var att in newAttachments)
                        {
                            var attachment = new AssignmentAttachment
                            {
                                AssignmentId = assignmentId,
                                Type = att.Type,
                                Title = att.Title,
                                Url = att.Url ?? string.Empty,
                                VideoId = att.VideoId,
                                FileName = att.FileName
                            };
                            _context.AssignmentAttachments.Add(attachment);
                        }
                    }
                }
                catch
                {
                    // ignore json errors
                }
            }

            if (attachedFiles != null && attachedFiles.Any())
            {
                var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "assignment-files", assignmentId.ToString());
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
                    var extension = Path.GetExtension(originalName);
                    var safeName = $"{Guid.NewGuid():N}{extension}";
                    var filePath = Path.Combine(uploadRoot, safeName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var relativeUrl = $"/uploads/assignment-files/{assignmentId}/{safeName}";

                    var attachment = new AssignmentAttachment
                    {
                        AssignmentId = assignmentId,
                        Type = "File",
                        Title = originalName,
                        FileName = originalName,
                        Url = relativeUrl
                    };

                    _context.AssignmentAttachments.Add(attachment);
                }
            }
        }

        private async Task RemoveAttachmentAsync(AssignmentAttachment attachment)
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
                        // ignore delete errors
                    }
                }
            }

            _context.AssignmentAttachments.Remove(attachment);
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Assignment assignment, string? attachmentsJson, string? removedAttachmentIds, List<IFormFile>? attachedFiles)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var existingAssignment = await _context.Assignments
                .Include(a => a.Class)
                .Include(a => a.Attachments)
                .FirstOrDefaultAsync(a => a.Id == assignment.Id);

            if (existingAssignment == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == existingAssignment.ClassId);

            if (enrollment == null || enrollment.Role != "Teacher")
            {
                return Forbid();
            }

            if (existingAssignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            if (string.IsNullOrWhiteSpace(assignment.Title))
            {
                TempData["ErrorMessage"] = "Tiêu đề bài tập không được để trống.";
                return RedirectToAction("Details", "Classes", new { id = existingAssignment.ClassId });
            }

            var now = DateTime.Now;
            var effectivePublishAt = assignment.PublishAt ?? existingAssignment.PublishAt ?? now;

            if (assignment.PublishAt.HasValue && assignment.PublishAt.Value < now.AddMinutes(-1))
            {
                TempData["ErrorMessage"] = "Ngày đăng bài không được trước thời điểm hiện tại.";
                return RedirectToAction("Details", "Classes", new { id = existingAssignment.ClassId });
            }

            if (assignment.DueDate.HasValue && assignment.DueDate.Value <= effectivePublishAt)
            {
                TempData["ErrorMessage"] = "Hạn nộp bài phải lớn hơn thời điểm đăng bài.";
                return RedirectToAction("Details", "Classes", new { id = existingAssignment.ClassId });
            }

            existingAssignment.Title = assignment.Title?.Trim();
            existingAssignment.Description = assignment.Description?.Trim();
            existingAssignment.DueDate = assignment.DueDate;
            existingAssignment.PublishAt = assignment.PublishAt ?? existingAssignment.PublishAt ?? now;

            if (!string.IsNullOrWhiteSpace(removedAttachmentIds))
            {
                try
                {
                    var ids = JsonSerializer.Deserialize<List<int>>(removedAttachmentIds, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<int>();

                    if (ids.Any())
                    {
                        var toRemove = existingAssignment.Attachments.Where(a => ids.Contains(a.Id)).ToList();
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

            await SaveAttachmentsAsync(existingAssignment.Id, attachmentsJson, attachedFiles);
            await _context.SaveChangesAsync();

            return RedirectToClassDetailsWithMessage(existingAssignment.ClassId, successMessage: "Bài tập đã được cập nhật thành công!");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Class)
                .Include(a => a.Attachments)
                .Include(a => a.Submissions)
                    .ThenInclude(s => s.Attachments)
                .Include(a => a.Submissions)
                    .ThenInclude(s => s.Comments)
                .Include(a => a.Comments)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (assignment == null)
            {
                return NotFound();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

            if (enrollment == null || enrollment.Role != "Teacher")
            {
                return Forbid();
            }

            if (assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            var classId = assignment.ClassId;

            // Xóa các submissions và dữ liệu liên quan trước (do FK_Submissions_Assignments là Restrict)
            if (assignment.Submissions != null && assignment.Submissions.Any())
            {
                foreach (var submission in assignment.Submissions.ToList())
                {
                    // Xóa comment gắn với submission này (bao gồm riêng tư)
                    if (submission.Comments != null && submission.Comments.Any())
                    {
                        _context.Comments.RemoveRange(submission.Comments.ToList());
                    }

                    // Xóa file đính kèm của submission (chỉ trong database, không động vào file vật lý vì hàm helper nằm ở SubmissionsController)
                    if (submission.Attachments != null && submission.Attachments.Any())
                    {
                        _context.SubmissionAttachments.RemoveRange(submission.Attachments.ToList());
                    }

                    _context.Submissions.Remove(submission);
                }
            }

            // Xóa comments gắn trực tiếp với assignment (nhận xét chung + riêng tư không có SubmissionId)
            if (assignment.Comments != null && assignment.Comments.Any())
            {
                _context.Comments.RemoveRange(assignment.Comments.ToList());
            }

            if (assignment.Attachments.Any())
            {
                foreach (var attachment in assignment.Attachments.ToList())
                {
                    await RemoveAttachmentAsync(attachment);
                }
            }

            _context.Assignments.Remove(assignment);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Classes", new { id = classId });
        }
        private IActionResult RedirectWithClassBlockedMessage()
        {
            TempData["ErrorMessage"] = ClassBlockedMessage;
            return RedirectToAction("Index", "Home");
        }

        private IActionResult RedirectToClassDetailsWithMessage(int classId, string? successMessage = null, string? errorMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                TempData["SuccessMessage"] = successMessage;
            }
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                TempData["ErrorMessage"] = errorMessage;
            }
            return RedirectToAction("Details", "Classes", new { id = classId });
        }
    }
}


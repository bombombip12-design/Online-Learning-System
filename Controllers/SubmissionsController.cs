using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using FinalASB.Models;
using System.Security.Claims;
using System.IO;
using System.Linq;

namespace FinalASB.Controllers
{
    [Authorize]
    public class SubmissionsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private const string ClassBlockedMessage = "Lớp học tạm thời bị khóa. Vui lòng thử lại sau!";

        public SubmissionsController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Create(int assignmentId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Class)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            if (assignment == null)
            {
                return NotFound();
            }

            if (assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

            if (enrollment == null || enrollment.Role != "Student")
            {
                return Forbid();
            }

            ViewBag.AssignmentId = assignmentId;
            ViewBag.Assignment = assignment;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int assignmentId, string driveFileId, string driveFileUrl)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Class)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            if (assignment == null)
            {
                return NotFound();
            }

            if (assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

            if (enrollment == null || enrollment.Role != "Student")
            {
                return Forbid();
            }

            // Check if submission already exists
            var submission = await _context.Submissions
                .Include(s => s.Attachments)
                .FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == userId);

            if (submission == null)
            {
                submission = new Submission
                {
                    AssignmentId = assignmentId,
                    StudentId = userId,
                    SubmittedAt = DateTime.Now
                };
                _context.Submissions.Add(submission);
                await _context.SaveChangesAsync();
            }
            else
            {
                submission.SubmittedAt = DateTime.Now;
            }

            if (!string.IsNullOrWhiteSpace(driveFileId) && !string.IsNullOrWhiteSpace(driveFileUrl))
            {
                var attachment = new SubmissionAttachment
                {
                    SubmissionId = submission.Id,
                    Type = "Link",
                    Title = driveFileId,
                    Url = driveFileUrl
                };
                _context.SubmissionAttachments.Add(attachment);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Assignments", new { id = assignmentId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitWork(int assignmentId, List<IFormFile>? files, List<string>? linkTitles, List<string>? linkUrls)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var assignment = await _context.Assignments
                .Include(a => a.Class)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

            if (assignment == null)
            {
                return NotFound();
            }

            if (assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            if (assignment.DueDate.HasValue && assignment.DueDate.Value < DateTime.Now)
            {
                TempData["ErrorMessage"] = "Không thể nộp bài tập sau ngày đến hạn.";
                return RedirectToAction("Details", "Assignments", new { id = assignmentId });
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == assignment.ClassId);

            if (enrollment == null || enrollment.Role != "Student")
            {
                return Forbid();
            }

            var submission = await _context.Submissions
                .Include(s => s.Attachments)
                .FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == userId);

            if (submission == null)
            {
                submission = new Submission
                {
                    AssignmentId = assignmentId,
                    StudentId = userId,
                    SubmittedAt = DateTime.Now
                };
                _context.Submissions.Add(submission);
                await _context.SaveChangesAsync();
            }
            else
            {
                submission.SubmittedAt = DateTime.Now;
            }

            if (files != null && files.Any())
            {
                var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "submission-attachments", submission.Id.ToString());
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

                    var relativeUrl = $"/uploads/submission-attachments/{submission.Id}/{safeName}";

                    var attachment = new SubmissionAttachment
                    {
                        SubmissionId = submission.Id,
                        Type = "File",
                        Title = originalName,
                        FileName = originalName,
                        Url = relativeUrl
                    };
                    _context.SubmissionAttachments.Add(attachment);
                }
            }

            if (linkUrls != null && linkUrls.Any())
            {
                for (int i = 0; i < linkUrls.Count; i++)
                {
                    var url = linkUrls[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var title = (linkTitles != null && i < linkTitles.Count ? linkTitles[i] : null)?.Trim();
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        title = url;
                    }

                    var submissionLink = new SubmissionAttachment
                    {
                        SubmissionId = submission.Id,
                        Type = "Link",
                        Title = title,
                        Url = url
                    };
                    _context.SubmissionAttachments.Add(submissionLink);
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Đã nộp bài thành công.";
            return RedirectToAction("Details", "Assignments", new { id = assignmentId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unsubmit(int assignmentId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var submission = await _context.Submissions
                .Include(s => s.Assignment)
                    .ThenInclude(a => a.Class)
                .Include(s => s.Attachments)
                .FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == userId);

            if (submission == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy bài nộp để huỷ.";
                return RedirectToAction("Details", "Assignments", new { id = assignmentId });
            }

            if (submission.Assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            if (submission.Assignment.DueDate.HasValue && submission.Assignment.DueDate.Value < DateTime.Now)
            {
                TempData["ErrorMessage"] = "Không thể huỷ nộp bài sau ngày đến hạn.";
                return RedirectToAction("Details", "Assignments", new { id = assignmentId });
            }

            // Set SubmissionId = null cho tất cả Comments liên quan (giữ lại Comments, đặc biệt là comment riêng tư)
            var relatedComments = await _context.Comments
                .Where(c => c.SubmissionId == submission.Id)
                .ToListAsync();
    
            foreach (var comment in relatedComments)
            {
                comment.SubmissionId = null;
            }

            // Xóa tất cả Attachments
            if (submission.Attachments.Any())
            {
                foreach (var attachment in submission.Attachments.ToList())
                {
                    RemoveSubmissionAttachmentFromDisk(attachment);
                    _context.SubmissionAttachments.Remove(attachment);
                }
            }

            _context.Submissions.Remove(submission);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đã huỷ nộp bài. Bạn có thể nộp lại nếu cần.";
            return RedirectToAction("Details", "Assignments", new { id = assignmentId });
        }

        [HttpPost]
        public async Task<IActionResult> Grade(int submissionId, int? score, string? teacherComment)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var submission = await _context.Submissions
                .Include(s => s.Assignment)
                    .ThenInclude(a => a.Class)
                .FirstOrDefaultAsync(s => s.Id == submissionId);

            if (submission == null)
            {
                return NotFound();
            }

            if (submission.Assignment.Class.IsBlock)
            {
                return RedirectWithClassBlockedMessage();
            }

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.UserId == userId && e.ClassId == submission.Assignment.ClassId);

            if (enrollment == null || enrollment.Role != "Teacher")
            {
                return Forbid();
            }

            // Validate điểm số: tối thiểu 0, tối đa 100
            if (score.HasValue && (score.Value < 0 || score.Value > 100))
            {
                TempData["ErrorMessage"] = "Điểm số phải từ 0 đến 100.";
                return RedirectToAction("Details", "Assignments", new { id = submission.AssignmentId });
            }

            submission.Score = score;
            submission.TeacherComment = teacherComment;

            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Assignments", new { id = submission.AssignmentId });
        }

        private void RemoveSubmissionAttachmentFromDisk(SubmissionAttachment attachment)
        {
            if (attachment.Type != "File" || string.IsNullOrWhiteSpace(attachment.Url))
            {
                return;
            }

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

        private IActionResult RedirectWithClassBlockedMessage()
        {
            TempData["ErrorMessage"] = ClassBlockedMessage;
            return RedirectToAction("Index", "Home");
        }
    }
}


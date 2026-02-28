using System.Security.Claims;
using FinalASB.Data;
using FinalASB.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinalASB.Controllers
{
    [Authorize]
    public class CommentsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CommentsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> List(int classId, string entityType, int entityId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.ClassId == classId && e.UserId == userId);

            if (enrollment == null)
            {
                return Forbid();
            }

            var query = _context.Comments
                .Include(c => c.User)
                .Where(c => c.ClassId == classId)
                .Where(c => !c.IsPrivate); // Chỉ lấy nhận xét công khai (không riêng tư)

            if (string.Equals(entityType, "assignment", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.AssignmentId == entityId);
            }
            else if (string.Equals(entityType, "announcement", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.AnnouncementId == entityId);
            }
            else
            {
                return BadRequest("Loại nội dung không hợp lệ.");
            }

            var comments = await query
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    id = c.Id,
                    userId = c.UserId,
                    user = c.User.FullName,
                    initials = string.IsNullOrWhiteSpace(c.User.FullName)
                        ? "?"
                        : c.User.FullName.Substring(0, 1).ToUpper(),
                    avatarUrl = c.User.AvatarUrl,
                    content = c.Content,
                    createdAt = c.CreatedAt
                })
                .ToListAsync();

            return Json(new 
            { 
                success = true, 
                data = comments,
                currentUserId = userId,
                currentUserRole = enrollment.Role
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int classId, string entityType, int entityId, string content, bool isPrivate = false, int? submissionId = null, int? targetUserId = null)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return Json(new { success = false, error = "Nội dung nhận xét không được để trống." });
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var enrollment = await _context.Enrollments
                .FirstOrDefaultAsync(e => e.ClassId == classId && e.UserId == userId);

            if (enrollment == null)
            {
                return Json(new { success = false, error = "Bạn không có quyền trong lớp học này." });
            }

            var comment = new Comment
            {
                ClassId = classId,
                UserId = userId,
                TargetUserId = targetUserId,
                Content = content.Trim(),
                IsPrivate = isPrivate,
                CreatedAt = DateTime.Now
            };

            if (string.Equals(entityType, "assignment", StringComparison.OrdinalIgnoreCase))
            {
                var assignment = await _context.Assignments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == entityId && a.ClassId == classId);

                if (assignment == null)
                {
                    return Json(new { success = false, error = "Bài tập không tồn tại." });
                }

                comment.AssignmentId = entityId;
                
                // If submissionId is provided, link the comment to that submission
                if (submissionId.HasValue)
                {
                    var submission = await _context.Submissions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == submissionId.Value && s.AssignmentId == entityId);
                    
                    if (submission != null)
                    {
                        comment.SubmissionId = submissionId.Value;
                    }
                }
            }
            else if (string.Equals(entityType, "announcement", StringComparison.OrdinalIgnoreCase))
            {
                var announcement = await _context.Announcements
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == entityId && a.ClassId == classId);

                if (announcement == null)
                {
                    return Json(new { success = false, error = "Thông báo không tồn tại." });
                }

                comment.AnnouncementId = entityId;
            }
            else
            {
                return Json(new { success = false, error = "Loại nội dung không hợp lệ." });
            }

            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(userId);

            return Json(new
            {
                success = true,
                comment = new
                {
                    id = comment.Id,
                    user = user?.FullName ?? "Bạn",
                    initials = string.IsNullOrWhiteSpace(user?.FullName)
                        ? "?"
                        : user!.FullName.Substring(0, 1).ToUpper(),
                    avatarUrl = user?.AvatarUrl,
                    content = comment.Content,
                    createdAt = comment.CreatedAt
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
                {
                    return Json(new { success = false, error = "Không thể xác định người dùng." });
                }

                // Kiểm tra id có hợp lệ không
                if (id <= 0)
                {
                    return Json(new { success = false, error = "ID bình luận không hợp lệ." });
                }

                // Tìm comment để kiểm tra quyền và xóa - không dùng AsNoTracking để có thể xóa
                var comment = await _context.Comments
                    .Include(c => c.User)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (comment == null)
                {
                    return Json(new { success = false, error = "Bình luận không tồn tại." });
                }

                if (comment.ClassId == null)
                {
                    return Json(new { success = false, error = "Không thể xác định lớp học của bình luận." });
                }

                // Kiểm tra user có trong lớp học không
                var enrollment = await _context.Enrollments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.ClassId == comment.ClassId && e.UserId == userId);

                if (enrollment == null)
                {
                    return Json(new { success = false, error = "Bạn không có quyền trong lớp học này." });
                }

                var isTeacher = enrollment.Role == "Teacher";
                var isCommentOwner = comment.UserId == userId;

                // Kiểm tra quyền xóa:
                // 1. User có thể xóa comment của chính mình (cả công khai và riêng tư)
                // 2. Giáo viên có thể xóa comment của học sinh (cả công khai và riêng tư)
                if (!isCommentOwner)
                {
                    if (!isTeacher)
                    {
                        return Json(new { success = false, error = "Bạn chỉ có thể xóa bình luận của chính mình." });
                    }

                    // Kiểm tra xem comment có thuộc về học sinh không
                    var commentOwnerEnrollment = await _context.Enrollments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.ClassId == comment.ClassId && e.UserId == comment.UserId);

                    // Nếu không tìm thấy enrollment của người tạo comment, có thể user đã bị xóa khỏi lớp
                    // Nhưng vẫn cho phép giáo viên xóa comment đó (có thể là học sinh đã bị xóa khỏi lớp)
                    // Chỉ từ chối nếu comment thuộc về giáo viên khác
                    if (commentOwnerEnrollment != null && commentOwnerEnrollment.Role == "Teacher")
                    {
                        return Json(new { success = false, error = "Bạn chỉ có thể xóa bình luận của học sinh." });
                    }
                }

                // Xóa comment (đã được tracked từ query ở trên)
                _context.Comments.Remove(comment);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa bình luận thành công." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"Đã xảy ra lỗi: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, string content)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
                {
                    return Json(new { success = false, error = "Không thể xác định người dùng." });
                }

                // Kiểm tra id có hợp lệ không
                if (id <= 0)
                {
                    return Json(new { success = false, error = "ID bình luận không hợp lệ." });
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return Json(new { success = false, error = "Nội dung nhận xét không được để trống." });
                }

                // Tìm comment để cập nhật
                var comment = await _context.Comments
                    .Include(c => c.User)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (comment == null)
                {
                    return Json(new { success = false, error = "Bình luận không tồn tại." });
                }

                if (comment.ClassId == null)
                {
                    return Json(new { success = false, error = "Không thể xác định lớp học của bình luận." });
                }

                // Kiểm tra user có trong lớp học không
                var enrollment = await _context.Enrollments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.ClassId == comment.ClassId && e.UserId == userId);

                if (enrollment == null)
                {
                    return Json(new { success = false, error = "Bạn không có quyền trong lớp học này." });
                }

                var isTeacher = enrollment.Role == "Teacher";
                var isCommentOwner = comment.UserId == userId;

                // Kiểm tra quyền chỉnh sửa:
                // Chỉ giáo viên mới có thể chỉnh sửa, và chỉ có thể sửa comment của chính mình
                if (!isTeacher)
                {
                    return Json(new { success = false, error = "Chỉ giáo viên mới có thể chỉnh sửa nhận xét." });
                }

                if (!isCommentOwner)
                {
                    return Json(new { success = false, error = "Bạn chỉ có thể chỉnh sửa nhận xét của chính mình." });
                }

                // Cập nhật nội dung comment
                comment.Content = content.Trim();
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã cập nhật nhận xét thành công.", content = comment.Content });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"Đã xảy ra lỗi: {ex.Message}" });
            }
        }
    }
}


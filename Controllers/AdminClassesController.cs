using FinalASB.Data;
using FinalASB.Models;
using FinalASB.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;

namespace FinalASB.Controllers
{
    public class AdminClassesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminClassesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Danh sách lớp
        public async Task<IActionResult> Index(string? search, string? status, string? isBlock)
        {
            // Update tất cả các bản ghi có Status NULL trước khi query
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Classes SET Status = 'Active' WHERE Status IS NULL");
            }
            catch
            {
                // Ignore nếu có lỗi
            }

            try
            {
                var query = _context.Classes
                    .Include(c => c.Owner)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var keyword = search.Trim().ToLower();
                    query = query.Where(c =>
                        c.ClassName.ToLower().Contains(keyword) ||
                        c.Owner.FullName.ToLower().Contains(keyword));
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    query = query.Where(c => c.Status == status);
                }

                if (!string.IsNullOrWhiteSpace(isBlock) && bool.TryParse(isBlock, out var blockValue))
                {
                    query = query.Where(c => c.IsBlock == blockValue);
                }

                var classes = await query
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                ViewBag.CurrentSearch = search;
                ViewBag.CurrentStatus = status;
                ViewBag.CurrentIsBlock = isBlock;

                return View(classes);
            }
            catch (Exception ex) when (ex.Message.Contains("Null") || (ex.InnerException?.Message.Contains("Null") ?? false))
            {
                // Nếu vẫn gặp lỗi NULL, update lại và retry
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Classes SET Status = 'Active' WHERE Status IS NULL");

                var query = _context.Classes
                    .Include(c => c.Owner)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var keyword = search.Trim().ToLower();
                    query = query.Where(c =>
                        c.ClassName.ToLower().Contains(keyword) ||
c.Owner.FullName.ToLower().Contains(keyword));
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    query = query.Where(c => c.Status == status);
                }

                if (!string.IsNullOrWhiteSpace(isBlock) && bool.TryParse(isBlock, out var blockValue))
                {
                    query = query.Where(c => c.IsBlock == blockValue);
                }

                var classes = await query
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                ViewBag.CurrentSearch = search;
                ViewBag.CurrentStatus = status;
                ViewBag.CurrentIsBlock = isBlock;

                return View(classes);
            }
        }

        // Chọn lớp để sửa
        public async Task<IActionResult> SelectToEdit()
        {
            // Update tất cả các bản ghi có Status NULL trước khi query
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Classes SET Status = 'Active' WHERE Status IS NULL");
            }
            catch
            {
                // Ignore nếu có lỗi
            }

            try
            {
                var classes = await _context.Classes
                            .Include(c => c.Owner)
                            .OrderByDescending(c => c.CreatedAt)
                            .ToListAsync();

                // Trả về List<Class> trực tiếp vào View
                return View(classes);
            }
            catch (Exception ex) when (ex.Message.Contains("Null") || (ex.InnerException?.Message.Contains("Null") ?? false))
            {
                // Nếu vẫn gặp lỗi NULL, update lại và retry
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Classes SET Status = 'Active' WHERE Status IS NULL");
                var classes = await _context.Classes.Include(c => c.Owner).ToListAsync();
                return View();
            }
        }

        [HttpPost]
        public IActionResult SelectToEdit(int classId)
        {
            if (classId <= 0)
            {
                return RedirectToAction(nameof(SelectToEdit));
            }
            return RedirectToAction(nameof(Edit), new { id = classId });
        }

        // Chọn lớp để xem bài tập và điểm
        public async Task<IActionResult> SelectToViewAssignments()
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Classes SET Status = 'Active' WHERE Status IS NULL");
            }
            catch { }

            try
            {
                //sửa
                var classes = await _context.Classes
                        .Include(c => c.Owner) // Cần phải Include Owner để hiển thị tên chủ lớp
                        .OrderByDescending(c => c.CreatedAt)
                        .ToListAsync();

                // Thay vì dùng ViewBag, bạn truyền Model trực tiếp
                return View(classes);
            }
            catch
            {
                var classes = await _context.Classes
                    .Include(c => c.Owner)
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();
                return View(classes);
            }
        }
        // Xem chi tiết lớp học với tất cả thông tin
        public async Task<IActionResult> ClassFullDetails(int id)
        {
            var cls = await _context.Classes
                .Include(c => c.Owner)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cls == null) return NotFound();

            // 1. Lấy thông báo (kèm file đính kèm và comment)
            var announcements = await _context.Announcements
                .Where(a => a.ClassId == id)
                .Include(a => a.User)

                // SỬA DÒNG NÀY: Phải đúng tên "AnnouncementAttachments" như trong Model
                .Include(a => a.Attachments)

                .Include(a => a.Comments)
                    .ThenInclude(c => c.User)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // 2. Lấy bài tập (kèm file và bài nộp)
            var assignments = await _context.Assignments
                .Where(a => a.ClassId == id)
                .Include(a => a.Creator)
                .Include(a => a.Submissions) // Để tính điểm
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // 3. Lấy danh sách học sinh
            var students = await _context.Enrollments
                .Where(e => e.ClassId == id && e.Role == "Student")
                .Include(e => e.User)
                .ToListAsync();

            // 4. Lấy nhận xét chung của lớp (nếu có comment trực tiếp vào ClassId)
            var classComments = await _context.Comments
                .Where(c => c.ClassId == id)
                .Include(c => c.User)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // Đưa vào ViewModel
            var model = new ClassFullDetailsViewModel
            {
                ClassInfo = cls,
                Announcements = announcements,
                Assignments = assignments,
                Students = students,
                ClassComments = classComments
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult SelectToViewAssignments(int classId)
        {
            if (classId <= 0)
            {
                return RedirectToAction(nameof(SelectToViewAssignments));
            }
            return RedirectToAction(nameof(Assignments), new { id = classId });
        }

        public IActionResult Edit(int id)
        {
            var cls = _context.Classes
                .Include(c => c.Owner)
                .FirstOrDefault(c => c.Id == id);
            if (cls == null) return NotFound();

            // Đảm bảo Status có giá trị
            if (string.IsNullOrEmpty(cls.Status))
            {
                cls.Status = "Active";
            }

            return View(cls);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Class cls)
        {
            // Loại bỏ lỗi validation cho Owner vì đây là navigation property, không cần validate
            ModelState.Remove("Owner");

            if (ModelState.IsValid)
            {
                var existingClass = await _context.Classes.FindAsync(cls.Id);
                if (existingClass == null) return NotFound();

                // Chỉ cập nhật các trường được phép chỉnh sửa
                existingClass.ClassName = cls.ClassName;
                existingClass.Description = cls.Description;
                existingClass.Status = cls.Status ?? "Active";
                // Giữ nguyên ClassUrl, OwnerId và các trường khác

                await _context.SaveChangesAsync();
                TempData["AdminSuccessMessage"] = $"Đã cập nhật thông tin lớp học: {existingClass.ClassName}";
                return RedirectToAction(nameof(SelectToEdit));
            }
            return View(cls);
        }

        // Khóa/ mở lớp
        public async Task<IActionResult> ToggleBlock(int id)
        {
            var cls = await _context.Classes.FindAsync(id);
            if (cls == null) return NotFound();

            // Chỉ đổi trạng thái khóa/mở, không đổi Status
            cls.IsBlock = !cls.IsBlock;
            await _context.SaveChangesAsync();

            var action = cls.IsBlock ? "khóa" : "mở";
            TempData["AdminSuccessMessage"] = $"Đã {action} lớp học: {cls.ClassName}";
            return RedirectToAction(nameof(Index));
        }

        // Xem bài tập và điểm của lớp (gộp chung)
        public async Task<IActionResult> Assignments(int id)
        {
            var cls = await _context.Classes.FindAsync(id);
            if (cls == null) return NotFound();

            var assignments = await _context.Assignments
                .Where(a => a.ClassId == id)
                .Include(a => a.Creator)
                .Include(a => a.Class)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var submissions = await _context.Submissions
                .Where(s => s.Assignment.ClassId == id)
                .Include(s => s.Student)
                .Include(s => s.Assignment)
                .OrderBy(s => s.Assignment.Title)
                .ThenBy(s => s.Student.FullName)
                .ToListAsync();
            ViewBag.Class = cls;
            ViewBag.Title = "Class Management";
            ViewBag.Submissions = submissions;
            return View(assignments);
        }

        // Xem chi tiết lớp học
        public async Task<IActionResult> Details(int id)
        {
            var cls = await _context.Classes
                .Include(c => c.Owner)
                    .ThenInclude(o => o.SystemRole)
                .Include(c => c.Enrollments)
                    .ThenInclude(e => e.User)
                        .ThenInclude(u => u.SystemRole)
                .Include(c => c.Assignments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cls == null) return NotFound();

            ViewBag.Title = "Class Management";
            return View(cls);
        }

        // Xem chi tiết bài tập (Admin)
        public async Task<IActionResult> AssignmentDetails(int id)
        {
            var assignment = await _context.Assignments
                .Include(a => a.Class)
                    .ThenInclude(c => c.Owner)
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

            // Load all submissions with their comments and files
            var allSubmissions = await _context.Submissions
                .Include(s => s.Student)
                .Include(s => s.Attachments)
                .Include(s => s.Comments)
                    .ThenInclude(c => c.User)
                .Where(s => s.AssignmentId == id)
                .OrderBy(s => s.Student.FullName)
                .ToListAsync();

            ViewBag.AllSubmissions = allSubmissions;

            return View("AdminAssignmentDetails", assignment);
        }
    }
}

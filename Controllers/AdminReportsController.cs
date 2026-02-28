using FinalASB.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace FinalASB.Controllers
{
    public class AdminReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Tổng số người dùng theo role
            var listUsers = await _context.Users
                .Where(u => u.SystemRoleId == 2) // Chỉ lấy User, bỏ Admin
                .OrderByDescending(u => u.CreatedAt) // Sắp xếp người mới nhất lên đầu
                .ToListAsync();

            // Số lớp đang hoạt động
            var activeClasses = await _context.Classes
                .CountAsync(c => c.Status == "Active" || c.Status == "Đang hoạt động");

            // Số bài nộp
            var totalSubmissions = await _context.Submissions.CountAsync();

            ViewBag.ListUsers = listUsers;
            ViewBag.ActiveClasses = activeClasses;
            ViewBag.TotalSubmissions = totalSubmissions;

            return View();
        }

        public async Task<IActionResult> Statistics(string period = "month")
        {
            var now = DateTime.Now;
            DateTime startDate;
            string periodLabel;

            switch (period.ToLower())
            {
                case "day":
                    startDate = now.Date;
                    periodLabel = "Hôm nay";
                    break;
                case "week":
                    startDate = now.AddDays(-7);
                    periodLabel = "7 ngày qua";
                    break;
                case "month":
                    startDate = new DateTime(now.Year, now.Month, 1);
                    periodLabel = "Tháng này";
                    break;
                case "year":
                    startDate = new DateTime(now.Year, 1, 1);
                    periodLabel = "Năm nay";
                    break;
                default:
                    startDate = new DateTime(now.Year, now.Month, 1);
                    periodLabel = "Tháng này";
                    break;
            }

            // Thống kê người dùng
            var newUsers = await _context.Users
                    .CountAsync(u => u.CreatedAt >= startDate && u.SystemRoleId == 2);

            // Thống kê lớp học
            var newClasses = await _context.Classes
                .CountAsync(c => c.CreatedAt >= startDate);

            // Thống kê thông báo
            var newAnnouncements = await _context.Announcements
                .CountAsync(a => a.CreatedAt >= startDate);

            // Thống kê bài tập
            var newAssignments = await _context.Assignments
                .CountAsync(a => a.CreatedAt >= startDate);

            // Thống kê bài nộp
            var newSubmissions = await _context.Submissions
                .CountAsync(s => s.SubmittedAt >= startDate);

            ViewBag.Period = period;
            ViewBag.PeriodLabel = periodLabel;
            ViewBag.StartDate = startDate;
            ViewBag.NewUsers = newUsers;
            ViewBag.NewClasses = newClasses;
            ViewBag.NewAnnouncements = newAnnouncements; // Chưa có bảng thống kê thông báo mới
            ViewBag.NewAssignments = newAssignments;
            ViewBag.NewSubmissions = newSubmissions;

            return View();
        }
    }
}

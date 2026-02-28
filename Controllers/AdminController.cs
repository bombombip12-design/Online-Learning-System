using FinalASB.Data;
using FinalASB.Models;
using FinalASB.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FinalASB.Controllers
{
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Dashboard()
        {
            var model = new AdminDashboardViewModel
            {
                TotalUsers = _context.Users.Where(u=>u.SystemRoleId == 2).Count(),
                TotalClasses = _context.Classes.Count(),
                TotalAnnouncements = _context.Announcements.Count(),
                TotalAssignments = _context.Assignments.Count(),
                TotalSubmissions = _context.Submissions.Count()
            };

            return View(model);
        }
    }
}
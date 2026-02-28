using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using System.Security.Claims;

namespace FinalASB.ViewComponents
{
    public class SidebarClassesViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public SidebarClassesViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(string? showType = null)
        {
            var userIdString = UserClaimsPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return View(new SidebarClassesViewModel
                {
                    TeachingClasses = new List<SidebarClassItem>(),
                    StudentClasses = new List<SidebarClassItem>(),
                    ShowType = showType
                });
            }

            var teachingClasses = await _context.Enrollments
                .Include(e => e.Class)
                .Where(e => e.UserId == userId && e.Role == "Teacher" && e.Class.Status == "Active" && e.Class.Status != "Non-active")
                .OrderByDescending(e => e.Class.CreatedAt)
                .Select(e => new SidebarClassItem
                {
                    Id = e.Class.Id,
                    ClassName = e.Class.ClassName
                })
                .ToListAsync();

            var studentClasses = await _context.Enrollments
                .Include(e => e.Class)
                .Where(e => e.UserId == userId && e.Role == "Student" && e.Class.Status == "Active" && e.Class.Status != "Non-active")
                .OrderByDescending(e => e.Class.CreatedAt)
                .Select(e => new SidebarClassItem
                {
                    Id = e.Class.Id,
                    ClassName = e.Class.ClassName
                })
                .ToListAsync();

            return View(new SidebarClassesViewModel
            {
                TeachingClasses = teachingClasses,
                StudentClasses = studentClasses,
                ShowType = showType
            });
        }
    }

    public class SidebarClassesViewModel
    {
        public List<SidebarClassItem> TeachingClasses { get; set; } = new();
        public List<SidebarClassItem> StudentClasses { get; set; } = new();
        public string? ShowType { get; set; }
    }

    public class SidebarClassItem
    {
        public int Id { get; set; }
        public string ClassName { get; set; } = string.Empty;
    }
}


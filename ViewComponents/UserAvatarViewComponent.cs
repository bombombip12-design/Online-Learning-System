using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinalASB.Data;
using System.Security.Claims;

namespace FinalASB.ViewComponents
{
    public class UserAvatarViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public UserAvatarViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userIdString = UserClaimsPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return View(new UserAvatarViewModel { AvatarUrl = null, Initials = "U" });
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return View(new UserAvatarViewModel { AvatarUrl = null, Initials = "U" });
            }

            var userName = user.FullName ?? "U";
            var initials = userName.Length > 0 ? userName.Substring(0, 1).ToUpper() : "U";

            return View(new UserAvatarViewModel { AvatarUrl = user.AvatarUrl, Initials = initials });
        }
    }

    public class UserAvatarViewModel
    {
        public string? AvatarUrl { get; set; }
        public string Initials { get; set; } = "U";
    }
}


using FinalASB.Models;

namespace FinalASB.ViewModels
{
    public class ClassFullDetailsViewModel
    {
        public Class ClassInfo { get; set; }
        public List<Announcement> Announcements { get; set; }
        public List<Assignment> Assignments { get; set; }
        public List<Enrollment> Students { get; set; }
        public List<Comment> ClassComments { get; set; }
    }
}

namespace FinalASB.Models
{
    public class Class
    {
        public int Id { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ClassUrl { get; set; }
        public bool IsBlock { get; set; } = false;
        public string Status { get; set; } = "Active";
        public string JoinCode { get; set; } = string.Empty;
        public string? ClassImageUrl { get; set; }
        public int OwnerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        public User Owner { get; set; } = null!;
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
        public ICollection<Announcement> Announcements { get; set; } = new List<Announcement>();
    }
}


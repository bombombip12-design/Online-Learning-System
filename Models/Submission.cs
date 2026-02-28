namespace FinalASB.Models
{
    public class Submission
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public int StudentId { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.Now;
        public int? Score { get; set; }
        public string? TeacherComment { get; set; }

        // Navigation properties
        public Assignment Assignment { get; set; } = null!;
        public User Student { get; set; } = null!;
        public ICollection<SubmissionAttachment> Attachments { get; set; } = new List<SubmissionAttachment>();
        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    }
}


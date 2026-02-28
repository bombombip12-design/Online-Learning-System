using System.ComponentModel.DataAnnotations.Schema;

namespace FinalASB.Models
{
    public class SubmissionAttachment
    {
        public int Id { get; set; }

        [Column("SubmissionsId")]
        public int SubmissionId { get; set; }

        public string Type { get; set; } = "File"; // File, Link
        public string Title { get; set; } = string.Empty;
        public string? Url { get; set; }
        public string? VideoId { get; set; }
        public string? FileName { get; set; }

        public Submission Submission { get; set; } = null!;
    }
}



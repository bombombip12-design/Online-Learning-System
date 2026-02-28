namespace FinalASB.Models
{
    public class AssignmentAttachment
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public string Type { get; set; } = string.Empty; // YouTube, Link, File
        public string Title { get; set; } = string.Empty;
        public string? Url { get; set; }
        public string? VideoId { get; set; }
        public string? FileName { get; set; }

        public Assignment Assignment { get; set; } = null!;
    }
}


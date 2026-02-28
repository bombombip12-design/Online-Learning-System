namespace FinalASB.Models
{
    public class AnnouncementAttachment
    {
        public int Id { get; set; }
        public int AnnouncementId { get; set; }
        public string Type { get; set; } = string.Empty; // YouTube, Link, File
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? VideoId { get; set; } // For YouTube
        public string? FileName { get; set; } // For File

        // Navigation property
        public Announcement Announcement { get; set; } = null!;
    }
}


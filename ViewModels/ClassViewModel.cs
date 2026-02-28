namespace FinalASB.ViewModels
{
    public class ClassViewModel
    {
        public int Id { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string JoinCode { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public bool IsBlock { get; set; }
        public String Status { get; set; } = string.Empty;
        public int StudentCount { get; set; }
        public string UserRole { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class CreateClassViewModel
    {
        public string ClassName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public IFormFile? ClassImageFile { get; set; }
    }
}


namespace FinalASB.ViewModels
{
    public class UserViewModel
    {
        public int Id { get; set; }

        public string FullName { get; set; }

        public string Email { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        // ⭐ Số lượng lớp đã đăng ký (Chỉ đếm lớp Active)
        public int EnrolledCount { get; set; }

        // ⭐ Số lượng lớp quản lý (chỉ đếm lớp Active)
        public int OwnedClasses { get; set; }
    }
}

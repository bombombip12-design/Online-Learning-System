namespace FinalASB.ViewModels
{
    public class UserCreateViewModel
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public int SystemRoleId { get; set; }

        public string Password { get; set; }
        public string ConfirmPassword { get; set; }
        public string AdminPassword { get; set; }
    }
}

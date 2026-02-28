using Microsoft.EntityFrameworkCore;
using FinalASB.Models;

namespace FinalASB.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<SystemRole> SystemRoles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Class> Classes { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AssignmentFile> AssignmentFiles { get; set; }
        public DbSet<Submission> Submissions { get; set; }
        public DbSet<SubmissionAttachment> SubmissionAttachments { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Announcement> Announcements { get; set; }
        public DbSet<AnnouncementFile> AnnouncementFiles { get; set; }
        public DbSet<AnnouncementAttachment> AnnouncementAttachments { get; set; }
        public DbSet<AssignmentAttachment> AssignmentAttachments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Configure Enrollment unique constraint
            modelBuilder.Entity<Enrollment>()
                .HasIndex(e => new { e.UserId, e.ClassId })
                .IsUnique();

            // Configure Submission unique constraint
            modelBuilder.Entity<Submission>()
                .HasIndex(s => new { s.AssignmentId, s.StudentId })
                .IsUnique();

            // Configure Enrollment Role check constraint (handled in application logic)
            modelBuilder.Entity<Enrollment>()
                .Property(e => e.Role)
                .HasMaxLength(20);

            // Configure Class Status default value
            modelBuilder.Entity<Class>()
                .Property(c => c.Status)
                .HasDefaultValue("Active")
                .IsRequired();

            // Configure Assignment - map Creator navigation to CreatedBy foreign key
            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Creator)
                .WithMany(u => u.CreatedAssignments)
                .HasForeignKey(a => a.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Class - map Owner navigation to OwnerId foreign key
            modelBuilder.Entity<Class>()
                .HasOne(c => c.Owner)
                .WithMany(u => u.OwnedClasses)
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Submission - map Student navigation to StudentId foreign key
            modelBuilder.Entity<Submission>()
                .HasOne(s => s.Student)
                .WithMany(u => u.Submissions)
                .HasForeignKey(s => s.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Comment - map Announcement navigation to AnnouncementId foreign key
            modelBuilder.Entity<Comment>()
                .HasOne(c => c.Announcement)
                .WithMany(a => a.Comments)
                .HasForeignKey(c => c.AnnouncementId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Comment - map TargetUser navigation to TargetUserId foreign key
            modelBuilder.Entity<Comment>()
                .HasOne(c => c.TargetUser)
                .WithMany()
                .HasForeignKey(c => c.TargetUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure AnnouncementFile relationship explicitly
            modelBuilder.Entity<AnnouncementFile>()
                .HasOne(af => af.Announcement)
                .WithMany(a => a.AnnouncementFiles)
                .HasForeignKey(af => af.AnnouncementId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AnnouncementAttachment relationship explicitly
            modelBuilder.Entity<AnnouncementAttachment>(entity =>
            {
                entity.ToTable("AnnouncementAttachments");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.AnnouncementId)
                    .HasColumnName("AnnouncementId")
                    .IsRequired();
                entity.HasOne(a => a.Announcement)
                    .WithMany(an => an.Attachments)
                    .HasForeignKey(a => a.AnnouncementId)
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AssignmentAttachment>()
                .HasOne(a => a.Assignment)
                .WithMany(an => an.Attachments)
                .HasForeignKey(a => a.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SubmissionAttachment>(entity =>
            {
                entity.ToTable("SubmissionsAttachments");
                entity.Property(a => a.SubmissionId).HasColumnName("SubmissionsId");
                entity.Property(a => a.Type).HasMaxLength(50);
                entity.Property(a => a.Title).HasMaxLength(255);
                entity.Property(a => a.Url).HasColumnType("nvarchar(max)");
                entity.Property(a => a.VideoId).HasMaxLength(100);
                entity.Property(a => a.FileName).HasMaxLength(255);
                entity.HasOne(a => a.Submission)
                    .WithMany(s => s.Attachments)
                    .HasForeignKey(a => a.SubmissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}


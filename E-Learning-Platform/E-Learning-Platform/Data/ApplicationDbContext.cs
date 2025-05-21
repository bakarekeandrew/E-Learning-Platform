using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using E_Learning_Platform.Models;

namespace E_Learning_Platform.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<CourseProgress> CourseProgress { get; set; }
        public DbSet<Certificate> Certificates { get; set; }
        public DbSet<UserActivity> UserActivities { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<CourseRating> CourseRatings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure relationships and constraints here
            builder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            builder.Entity<Course>()
                .HasOne(c => c.Instructor)
                .WithMany()
                .HasForeignKey(c => c.InstructorId);

            builder.Entity<Enrollment>()
                .HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            builder.Entity<Enrollment>()
                .HasOne(e => e.Course)
                .WithMany()
                .HasForeignKey(e => e.CourseId);

            // Configure CourseProgress entity
            builder.Entity<CourseProgress>()
                .HasKey(cp => cp.ProgressId);

            // Configure CourseRating entity
            builder.Entity<CourseRating>()
                .HasKey(cr => cr.RatingId);

            builder.Entity<CourseRating>()
                .HasOne(cr => cr.Course)
                .WithMany(c => c.Ratings)
                .HasForeignKey(cr => cr.CourseId);

            // Configure UserActivity entity
            builder.Entity<UserActivity>()
                .HasKey(ua => ua.ActivityId);

            builder.Entity<UserActivity>()
                .HasOne(ua => ua.User)
                .WithMany()
                .HasForeignKey(ua => ua.UserId);
        }
    }
} 
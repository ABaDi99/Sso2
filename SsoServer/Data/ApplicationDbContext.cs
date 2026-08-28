using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SsoServer.Entities.Identity;

namespace SsoServer.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<UserApplicationRole> UserApplicationRoles => Set<UserApplicationRole>();
    public DbSet<UserSuspension> UserSuspensions => Set<UserSuspension>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserApplicationRole>(entity =>
        {
            // Un même rôle ne peut pas être assigné deux fois au même
            // utilisateur pour la même application.
            entity.HasIndex(x => new { x.UserId, x.ClientId, x.RoleId }).IsUnique();

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Role)
                  .WithMany()
                  .HasForeignKey(x => x.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSuspension>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>();

            // Accélère la question posée à chaque authentification :
            // "cet utilisateur a-t-il une suspension active maintenant ?"
            entity.HasIndex(x => new { x.UserId, x.DateDebut, x.DateFin });

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
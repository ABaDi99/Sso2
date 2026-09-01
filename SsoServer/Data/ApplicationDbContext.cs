using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SsoServer.Entities.Identity;

namespace SsoServer.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<UserApplicationRole> UserApplicationRoles => Set<UserApplicationRole>();
    public DbSet<UserSuspension> UserSuspensions => Set<UserSuspension>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            // Identity impose par défaut un nom de rôle unique globalement
            // (index "RoleNameIndex" sur NormalizedName seul). Un rôle
            // appartenant désormais à une application précise, deux
            // applications doivent pouvoir avoir chacune leur "Manager"
            // sans collision — on retire l'unicité globale au profit d'une
            // unicité (nom, application). ClientId nul (le rôle Admin,
            // seul rôle global) reste de fait unique en pratique : on ne
            // laisse la création d'un second rôle sans application que
            // par une intervention directe en base, pas via l'API.
            entity.HasIndex(r => r.NormalizedName).IsUnique(false);
            entity.HasIndex(r => new { r.NormalizedName, r.ClientId }).IsUnique();
        });

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
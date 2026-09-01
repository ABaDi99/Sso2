using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SsoServer.Entities.Identity;

namespace SsoServer.Security;

/// <summary>
/// Remplace le RoleValidator par défaut d'ASP.NET Identity : celui-ci
/// refuse tout nom de rôle déjà pris, sans regarder ClientId — incompatible
/// avec un modèle où deux applications peuvent chacune avoir un rôle du
/// même nom. L'unicité attendue ici est (nom, application), pas juste (nom).
/// </summary>
public sealed class ApplicationRoleValidator : IRoleValidator<ApplicationRole>
{
    public async Task<IdentityResult> ValidateAsync(RoleManager<ApplicationRole> manager, ApplicationRole role)
    {
        var errors = new List<IdentityError>();

        if (string.IsNullOrWhiteSpace(role.Name))
        {
            errors.Add(new IdentityError
            {
                Code = "InvalidRoleName",
                Description = "Le nom du rôle est obligatoire."
            });
        }
        else
        {
            var normalized = manager.NormalizeKey(role.Name);

            var duplicate = await manager.Roles.AnyAsync(r =>
                r.Id != role.Id && r.NormalizedName == normalized && r.ClientId == role.ClientId);

            if (duplicate)
            {
                errors.Add(new IdentityError
                {
                    Code = "DuplicateRoleName",
                    Description = role.ClientId is null
                        ? $"Le rôle « {role.Name} » existe déjà."
                        : $"Le rôle « {role.Name} » existe déjà pour cette application."
                });
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}

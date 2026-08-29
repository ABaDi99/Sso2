using Microsoft.AspNetCore.Identity;
using SsoServer.Data;
using SsoServer.Entities.Identity;

namespace SsoServer.Security;

public enum LoginOutcome
{
    Success,

    // Identifiants incorrects.
    InvalidCredentials,

    // Désactivé par un administrateur ou suspendu (AccountStatusChecker).
    Blocked,

    // Verrouillage temporaire automatique d'ASP.NET Identity après
    // plusieurs mots de passe erronés — distinct de Blocked : un même
    // message pour les deux laisserait croire à un utilisateur qui s'est
    // juste trompé de mot de passe que son compte a été coupé.
    TemporarilyLocked
}

public sealed record LoginResult(LoginOutcome Outcome, string? Message);

/// <summary>
/// Tentative de connexion par mot de passe, partagée entre le formulaire
/// de connexion (/Account/Login, qui sert le flow OAuth) et l'endpoint de
/// connexion JSON (/api/account/login, utilisé par sso-admin) — les deux
/// doivent appliquer exactement le même contrôle de statut de compte.
///
/// Ce partage a été introduit après qu'un correctif appliqué à un seul des
/// deux points d'entrée (le message distinguant verrouillage temporaire et
/// désactivation) avait laissé l'autre avec l'ancien comportement : la
/// duplication avait divergé sans que personne ne s'en aperçoive.
/// </summary>
public sealed class AccountLoginService
{
    public async Task<LoginResult> AttemptLoginAsync(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        string email,
        string password)
    {
        var candidate = await userManager.FindByEmailAsync(email);
        if (candidate is not null)
        {
            var status = await AccountStatusChecker.CheckAsync(db, candidate);
            if (status.IsBlocked)
                return new LoginResult(LoginOutcome.Blocked, status.Message ?? "Ce compte est bloqué.");
        }

        var result = await signInManager.PasswordSignInAsync(
            email, password, isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            // IsLockedOut implique que PasswordSignInAsync a retrouvé un
            // compte pour cet email ; candidate (cherché plus haut) ne peut
            // donc pas être nul ici — mais on l'affirme via ??= plutôt qu'un
            // ! pour ne pas dépendre silencieusement de cette invariante si
            // le code autour venait à changer.
            candidate ??= await userManager.FindByEmailAsync(email);
            var lockoutEnd = candidate is null
                ? null
                : await userManager.GetLockoutEndDateAsync(candidate);
            var minutes = lockoutEnd is null
                ? (int?)null
                : Math.Max(1, (int)Math.Ceiling((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes));

            var message = minutes is null
                ? "Trop de tentatives échouées. Réessayez plus tard."
                : $"Trop de tentatives échouées. Réessayez dans {minutes} minute{(minutes > 1 ? "s" : "")}.";

            return new LoginResult(LoginOutcome.TemporarilyLocked, message);
        }

        if (!result.Succeeded)
            return new LoginResult(LoginOutcome.InvalidCredentials, "Email ou mot de passe incorrect.");

        return new LoginResult(LoginOutcome.Success, null);
    }
}

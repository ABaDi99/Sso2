using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SsoServer.Data;
using SsoServer.Entities.Identity;
using SsoServer.Security;

namespace SsoServer.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
    }

    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        // La suspension n'est jamais posée dans LockoutEnd (voir
        // AccountStatusChecker) : elle doit être vérifiée explicitement,
        // avant même de tenter le mot de passe.
        var candidate = await _userManager.FindByEmailAsync(Email);
        if (candidate is not null)
        {
            var status = await AccountStatusChecker.CheckAsync(_db, candidate);
            if (status.IsBlocked)
            {
                ErrorMessage = status.Message;
                return Page();
            }
        }

        var result = await _signInManager.PasswordSignInAsync(
            Email, Password, isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            ErrorMessage = "Ce compte a été désactivé.";
            return Page();
        }

        if (!result.Succeeded)
        {
            ErrorMessage = "Email ou mot de passe incorrect.";
            return Page();
        }

        return LocalRedirect(returnUrl ?? "/");
    }
}
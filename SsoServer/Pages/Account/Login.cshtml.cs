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
    private readonly AccountLoginService _loginService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        AccountLoginService loginService,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _loginService = loginService;
        _logger = logger;
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
        var login = await _loginService.AttemptLoginAsync(
            _signInManager, _userManager, _db, Email, Password);

        if (login.Outcome != LoginOutcome.Success)
        {
            _logger.LogWarning("Connexion refusée pour {Email} : {Outcome}.", Email, login.Outcome);
            ErrorMessage = login.Message;
            return Page();
        }

        return LocalRedirect(returnUrl ?? "/");
    }
}

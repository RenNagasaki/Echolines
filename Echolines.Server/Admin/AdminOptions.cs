namespace Echolines.Server.Admin;

public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// PBKDF2 hash of the single operator password, in the format written by
    /// <see cref="AdminPasswordHasher.Hash"/>. Comes from a secret / environment variable
    /// (<c>Admin__PasswordHash</c>) — never from a file in the repository.
    /// </summary>
    /// <remarks>
    /// Empty means the admin UI refuses every login. That is deliberate: an admin surface on the open
    /// internet without a password would be the single largest mistake in this design — whoever found it
    /// could release arbitrary lines and cause exactly the poisoning the quarantine was built against.
    /// </remarks>
    public string PasswordHash { get; set; } = "";

    /// <summary>How long a login lasts before the operator has to sign in again.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Login attempts per minute per IP. Brute force is the only attack a single account has.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 5;
}

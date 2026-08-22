using Microsoft.AspNetCore.Identity;
using ShopHub.Modules.Identity.Domain;

namespace ShopHub.Modules.Identity.Infrastructure;

/// <summary>Hashes and verifies passwords (spec §2 DECISION: <c>PasswordHasher&lt;T&gt;</c>).</summary>
internal interface IPasswordService
{
    string Hash(string password);

    PasswordVerificationResult Verify(string hash, string password);

    /// <summary>
    /// Verifies against a throwaway hash so an unknown email costs the same time as a wrong
    /// password. Without this, response timing reveals which emails have accounts.
    /// </summary>
    void VerifyDummy(string password);
}

/// <summary>
/// Wraps ASP.NET Core's <see cref="PasswordHasher{TUser}"/>. Full ASP.NET Core Identity is
/// deliberately not used (spec §2) - its schema fights the modular schema layout.
/// </summary>
internal sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>
    /// A precomputed hash of a fixed string, used only to burn equivalent CPU time on the
    /// unknown-email path. Computed once at construction.
    /// </summary>
    private readonly string _dummyHash;

    public PasswordService() =>
        _dummyHash = _hasher.HashPassword(null!, "constant-time-placeholder-value");

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordVerificationResult Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(null!, hash, password);

    public void VerifyDummy(string password) => _hasher.VerifyHashedPassword(null!, _dummyHash, password);
}

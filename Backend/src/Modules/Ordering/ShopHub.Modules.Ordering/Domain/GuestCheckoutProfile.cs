using ShopHub.Shared.Kernel.Entities;

namespace ShopHub.Modules.Ordering.Domain;

/// <summary>
/// A guest checkout identity, keyed on email (spec §8.3, §8.4 state 4).
/// <para>
/// <see cref="LinkedUserId"/> is set only when that email later <em>registers</em> - never
/// at checkout. Auto-linking an order on the strength of an unverified email is an
/// account-takeover vector, which is also why ADR-001 leaves guest email unverified and
/// shows a soft prompt instead.
/// </para>
/// </summary>
internal sealed class GuestCheckoutProfile : Entity
{
    private GuestCheckoutProfile()
    {
    }

    private GuestCheckoutProfile(
        Guid id,
        string email,
        string normalizedEmail,
        string fullName,
        string? phoneNumber,
        ShippingAddress address,
        PaymentMethod preferredPaymentMethod,
        DateTime nowUtc)
        : base(id)
    {
        Email = email;
        NormalizedEmail = normalizedEmail;
        FullName = fullName;
        PhoneNumber = phoneNumber;
        Line1 = address.Line1;
        Line2 = address.Line2;
        City = address.City;
        State = address.State;
        PostalCode = address.PostalCode;
        Country = address.Country;
        PreferredPaymentMethod = preferredPaymentMethod;
        FirstSeenUtc = nowUtc;
        LastUsedUtc = nowUtc;
        OrderCount = 0;
    }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Upper-cased, uniquely indexed. The upsert key.</summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string? PhoneNumber { get; private set; }

    public string Line1 { get; private set; } = string.Empty;

    public string? Line2 { get; private set; }

    public string City { get; private set; } = string.Empty;

    public string? State { get; private set; }

    public string PostalCode { get; private set; } = string.Empty;

    public string Country { get; private set; } = string.Empty;

    public PaymentMethod PreferredPaymentMethod { get; private set; }

    public DateTime FirstSeenUtc { get; private set; }

    public DateTime LastUsedUtc { get; private set; }

    public int OrderCount { get; private set; }

    /// <summary>
    /// Set at registration time if this email later becomes an account, so analytics can
    /// join guest history to the account without rewriting order rows (spec §8.4).
    /// </summary>
    public Guid? LinkedUserId { get; private set; }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public static GuestCheckoutProfile Create(
        string email,
        string fullName,
        string? phoneNumber,
        ShippingAddress address,
        PaymentMethod preferredPaymentMethod,
        DateTime nowUtc) =>
        new(
            SequentialGuid.New(),
            email.Trim(),
            Normalize(email),
            fullName.Trim(),
            phoneNumber?.Trim(),
            address,
            preferredPaymentMethod,
            nowUtc);

    /// <summary>
    /// Refreshes the profile from the latest checkout form and counts the order
    /// (spec §8.4: "increment OrderCount, update LastUsedUtc").
    /// </summary>
    public void RecordCheckout(
        string fullName,
        string? phoneNumber,
        ShippingAddress address,
        PaymentMethod preferredPaymentMethod,
        DateTime nowUtc)
    {
        FullName = fullName.Trim();
        PhoneNumber = phoneNumber?.Trim();
        Line1 = address.Line1;
        Line2 = address.Line2;
        City = address.City;
        State = address.State;
        PostalCode = address.PostalCode;
        Country = address.Country;
        PreferredPaymentMethod = preferredPaymentMethod;
        LastUsedUtc = nowUtc;
        OrderCount++;
    }

    /// <summary>Called when a user registers with this email (spec §8.4).</summary>
    public void LinkToUser(Guid userId) => LinkedUserId = userId;

    public ShippingAddress ToShippingAddress() =>
        new(FullName, Line1, Line2, City, State, PostalCode, Country, PhoneNumber);
}

namespace ShopHub.Shared.Kernel.Entities;

/// <summary>
/// Primary-key factory for business entities (spec A5: sequential v7 GUIDs, to keep
/// clustered-index inserts append-only instead of fragmenting the B-tree).
/// </summary>
/// <remarks>
/// VERIFIED at install time (2026-08-23, .NET SDK 10.0.301 / runtime 10.0.9):
/// <c>Guid.CreateVersion7()</c> exists in the BCL, so the fallback helper the spec
/// allowed for is not needed. This type stays as the single seam anyway - if the PK
/// strategy ever changes, it changes here and nowhere else.
/// </remarks>
public static class SequentialGuid
{
    /// <summary>Creates a version-7 (timestamp-ordered) GUID.</summary>
    public static Guid New() => Guid.CreateVersion7();

    /// <summary>Creates a version-7 GUID stamped with an explicit time, for deterministic seeding and tests.</summary>
    public static Guid New(DateTimeOffset timestamp) => Guid.CreateVersion7(timestamp);
}

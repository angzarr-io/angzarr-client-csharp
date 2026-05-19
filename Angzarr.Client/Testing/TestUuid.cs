namespace Angzarr.Client.Testing;

/// <summary>
/// Deterministic UUID generation for tests.
///
/// <para>The same <c>name</c>/<c>namespace</c> pair always yields the same UUID,
/// matching Python's <c>angzarr_client.testing.uuid</c> and Rust's
/// <c>testing::uuid</c> byte-for-byte.</para>
///
/// <para>Audit finding #50 added the <c>UuidForDefault</c> ergonomic wrappers
/// that bind <see cref="DefaultTestNamespace"/> for the common test case.</para>
/// </summary>
public static class TestUuid
{
    /// <summary>
    /// Default namespace for test UUIDs. Equals Python's
    /// <c>DEFAULT_TEST_NAMESPACE</c>.
    /// </summary>
    public static readonly Guid DefaultTestNamespace =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    /// <summary>
    /// Generate a deterministic 16-byte UUID from a name. Returns raw bytes
    /// suitable for use as aggregate root IDs (network byte order).
    /// </summary>
    public static byte[] UuidFor(string name, Guid namespaceId)
    {
        return Identity.ToProtoBytes(NewUuidV5(namespaceId, name));
    }

    /// <summary>Default-namespace overload; mirrors Python's <c>uuid_for(name)</c>.</summary>
    public static byte[] UuidFor(string name) => UuidFor(name, DefaultTestNamespace);

    /// <summary>
    /// Audit finding #50 — ergonomic wrapper for <see cref="UuidFor(string)"/>
    /// matching Rust's <c>uuid_for_default</c> name.
    /// </summary>
    public static byte[] UuidForDefault(string name) => UuidFor(name, DefaultTestNamespace);

    /// <summary>Generate a deterministic UUID string from a name.</summary>
    public static string UuidStrFor(string name, Guid namespaceId)
    {
        return NewUuidV5(namespaceId, name).ToString();
    }

    /// <summary>Default-namespace overload.</summary>
    public static string UuidStrFor(string name) => UuidStrFor(name, DefaultTestNamespace);

    /// <summary>
    /// Audit finding #50 — ergonomic wrapper for <see cref="UuidStrFor(string)"/>.
    /// </summary>
    public static string UuidStrForDefault(string name) =>
        UuidStrFor(name, DefaultTestNamespace);

    /// <summary>Generate a deterministic <see cref="Guid"/> from a name.</summary>
    public static Guid UuidObjFor(string name, Guid namespaceId) =>
        NewUuidV5(namespaceId, name);

    /// <summary>Default-namespace overload.</summary>
    public static Guid UuidObjFor(string name) => UuidObjFor(name, DefaultTestNamespace);

    /// <summary>
    /// Audit finding #50 — ergonomic wrapper for <see cref="UuidObjFor(string)"/>.
    /// </summary>
    public static Guid UuidObjForDefault(string name) =>
        UuidObjFor(name, DefaultTestNamespace);

    // UUID v5 generation is shared with Identity (single source of truth).
    private static Guid NewUuidV5(Guid namespaceId, string name) =>
        Identity.NewUuidV5(namespaceId, name);
}

using System.Security.Cryptography;
using System.Text;

namespace Angzarr.Client;

/// <summary>
/// Aggregate identity computation for Angzarr domains.
///
/// <para>Provides deterministic UUID generation from business keys, ensuring
/// consistent aggregate identification across services. Mirrors the Python
/// <c>angzarr_client.identity</c> module and the Rust <c>identity</c> module
/// byte-for-byte.</para>
/// </summary>
public static class Identity
{
    /// <summary>
    /// RFC 4122 OID namespace UUID. Used by <see cref="ComputeRoot"/>.
    /// </summary>
    public static readonly Guid NamespaceOid =
        Guid.Parse("6ba7b812-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// RFC 4122 DNS namespace UUID. Matches Python's
    /// <c>INVENTORY_PRODUCT_NAMESPACE</c> and Rust's
    /// <c>INVENTORY_PRODUCT_NAMESPACE</c>.
    /// </summary>
    public static readonly Guid InventoryProductNamespace =
        Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// Compute a deterministic root UUID from domain and business key.
    ///
    /// <para>Mirrors Python's <c>compute_root</c>:
    /// <c>uuid5(NAMESPACE_OID, "angzarr" + domain + business_key)</c>.</para>
    /// </summary>
    public static Guid ComputeRoot(string domain, string businessKey)
    {
        var seed = "angzarr" + domain + businessKey;
        return NewUuidV5(NamespaceOid, seed);
    }

    /// <summary>
    /// Deterministic UUID for an inventory product aggregate.
    /// Uses the DNS namespace (matches Python <c>INVENTORY_PRODUCT_NAMESPACE</c>).
    /// </summary>
    public static Guid InventoryProductRoot(string productId)
    {
        return NewUuidV5(InventoryProductNamespace, productId);
    }

    /// <summary>Deterministic root UUID for a customer aggregate.</summary>
    public static Guid CustomerRoot(string email) => ComputeRoot("customer", email);

    /// <summary>Deterministic root UUID for a product aggregate.</summary>
    public static Guid ProductRoot(string sku) => ComputeRoot("product", sku);

    /// <summary>Deterministic root UUID for an order aggregate.</summary>
    public static Guid OrderRoot(string orderId) => ComputeRoot("order", orderId);

    /// <summary>Deterministic root UUID for an inventory aggregate.</summary>
    public static Guid InventoryRoot(string productId) => ComputeRoot("inventory", productId);

    /// <summary>Deterministic root UUID for a cart aggregate.</summary>
    public static Guid CartRoot(string customerId) => ComputeRoot("cart", customerId);

    /// <summary>Deterministic root UUID for a fulfillment aggregate.</summary>
    public static Guid FulfillmentRoot(string orderId) => ComputeRoot("fulfillment", orderId);

    /// <summary>
    /// Convert a UUID to its 16-byte network-order proto representation.
    /// </summary>
    public static byte[] ToProtoBytes(Guid id)
    {
        // System.Guid.ToByteArray() returns mixed-endian; we need big-endian
        // (the canonical wire representation matching Python/Rust uuid bytes).
        var bytes = id.ToByteArray();
        SwapToBigEndian(bytes);
        return bytes;
    }

    private static void SwapToBigEndian(byte[] b)
    {
        if (b.Length != 16)
            return;
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }

    /// <summary>
    /// Generate a name-based (v5/SHA-1) UUID per RFC 4122 §4.3.
    ///
    /// <para>Made internal so <see cref="Testing.TestUuid"/> can share it.</para>
    /// </summary>
    internal static Guid NewUuidV5(Guid namespaceId, string name)
    {
        var nsBytes = namespaceId.ToByteArray();
        SwapToBigEndian(nsBytes); // .NET Guid is mixed-endian; UUID v5 wants big-endian.
        var nameBytes = Encoding.UTF8.GetBytes(name);

#pragma warning disable SYSLIB0021 // SHA1 is mandated by RFC 4122 §4.3 for UUID v5.
        using var sha1 = SHA1.Create();
        sha1.TransformBlock(nsBytes, 0, 16, null, 0);
        sha1.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
        var hash = sha1.Hash!;
#pragma warning restore SYSLIB0021

        var uuidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, uuidBytes, 0, 16);

        // Set version (5: name-based using SHA-1).
        uuidBytes[6] = (byte)((uuidBytes[6] & 0x0F) | 0x50);
        // Set variant (RFC 4122).
        uuidBytes[8] = (byte)((uuidBytes[8] & 0x3F) | 0x80);

        // System.Guid expects the mixed-endian layout, so swap back from
        // big-endian to .NET's internal layout.
        var dotnetBytes = new byte[16];
        Buffer.BlockCopy(uuidBytes, 0, dotnetBytes, 0, 16);
        SwapToBigEndian(dotnetBytes);
        return new Guid(dotnetBytes);
    }
}

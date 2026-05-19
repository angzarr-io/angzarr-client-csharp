using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for Identity — ports the Python/Rust identity module byte-for-byte.
///
/// Deterministic UUIDs derived from business keys ensure consistent
/// aggregate identification across services and languages.
/// </summary>
public class IdentityTests
{
    [Fact]
    public void ComputeRoot_MatchesPython()
    {
        // Verified byte-equal with Python's:
        //   compute_root("player", "alice@x.com") = 8cf1fb5d-45ce-58c2-a7e4-34359eb42d7c
        // (also pinned in Rust: `compute_root_matches_python`).
        var id = Identity.ComputeRoot("player", "alice@x.com");

        id.ToString().Should().Be("8cf1fb5d-45ce-58c2-a7e4-34359eb42d7c");
    }

    [Fact]
    public void ComputeRoot_IsDeterministic()
    {
        Identity.ComputeRoot("order", "o-1").Should().Be(Identity.ComputeRoot("order", "o-1"));
    }

    [Fact]
    public void ComputeRoot_VariesByDomain()
    {
        Identity.ComputeRoot("customer", "x").Should().NotBe(Identity.ComputeRoot("product", "x"));
    }

    [Fact]
    public void DomainHelpers_DelegateToComputeRoot()
    {
        Identity.CustomerRoot("e").Should().Be(Identity.ComputeRoot("customer", "e"));
        Identity.ProductRoot("s").Should().Be(Identity.ComputeRoot("product", "s"));
        Identity.OrderRoot("o").Should().Be(Identity.ComputeRoot("order", "o"));
        Identity.InventoryRoot("p").Should().Be(Identity.ComputeRoot("inventory", "p"));
        Identity.CartRoot("c").Should().Be(Identity.ComputeRoot("cart", "c"));
        Identity.FulfillmentRoot("o").Should().Be(Identity.ComputeRoot("fulfillment", "o"));
    }

    [Fact]
    public void InventoryProductNamespace_IsDns()
    {
        // 6ba7b810-9dad-11d1-80b4-00c04fd430c8 is the RFC 4122 DNS namespace.
        Identity
            .InventoryProductNamespace.ToString()
            .Should()
            .Be("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    }

    [Fact]
    public void InventoryProductRoot_UsesDnsNamespace()
    {
        // Different namespace than ComputeRoot — should produce a different UUID
        // than `ComputeRoot("inventory", product_id)`.
        var byProduct = Identity.InventoryProductRoot("widget-1");
        var byInventory = Identity.InventoryRoot("widget-1");

        byProduct.Should().NotBe(byInventory);
    }

    [Fact]
    public void ToProtoBytes_Returns16()
    {
        var id = Identity.ComputeRoot("x", "y");
        Identity.ToProtoBytes(id).Length.Should().Be(16);
    }

    // Cross-language fixtures from features/client/identity.feature
    // (C-0110/C-0111/C-0114). Pinning these ensures the C# UUID v5
    // implementation matches Python/Rust byte-for-byte.

    [Theory]
    [InlineData("cart", "alice", "f520dbd7-0692-5a5a-b315-48c73f2fff1b")]
    [InlineData("order", "ord-42", "1e941e06-245c-5be9-9885-45852f029d0d")]
    [InlineData("order", "", "b6408065-482a-5d1a-9aac-ef4bb488f3b7")]
    public void ComputeRoot_MatchesCrossLanguageFixtures(string domain, string key, string expected)
    {
        Identity.ComputeRoot(domain, key).ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("customer_root", "alice@x.com", "9141d644-0602-5762-a8b9-d74e7d5a3d45")]
    [InlineData("product_root", "SKU-001", "25541820-eb7c-559d-9d00-834865d6ba57")]
    [InlineData("order_root", "ord-42", "1e941e06-245c-5be9-9885-45852f029d0d")]
    [InlineData("inventory_root", "prod-7", "af78f0ed-83a9-58aa-9f7b-53b253dd7242")]
    [InlineData("cart_root", "cust-9", "26e1f44e-eac8-550f-a738-4473dca718e5")]
    [InlineData("fulfillment_root", "ord-42", "ea29617a-0b9d-5c36-aefb-aec161b3cb34")]
    [InlineData("inventory_product_root", "sku-xyz", "8c6baabf-71a0-5b46-b953-ec3bdac0a995")]
    public void DomainRootHelpers_MatchCrossLanguageFixtures(string helper, string input, string expected)
    {
        var id = helper switch
        {
            "customer_root" => Identity.CustomerRoot(input),
            "product_root" => Identity.ProductRoot(input),
            "order_root" => Identity.OrderRoot(input),
            "inventory_root" => Identity.InventoryRoot(input),
            "cart_root" => Identity.CartRoot(input),
            "fulfillment_root" => Identity.FulfillmentRoot(input),
            "inventory_product_root" => Identity.InventoryProductRoot(input),
            _ => throw new ArgumentOutOfRangeException(nameof(helper)),
        };
        id.ToString().Should().Be(expected);
    }

    [Fact]
    public void InventoryProductRoot_DiffersFromComputeRootWithSameKey_PerC0112()
    {
        // C-0112: inventory_product_root uses the DNS namespace directly
        // (no "angzarr" prefix); compute_root("inventory_product", k) does
        // not — they must differ.
        Identity.ComputeRoot("inventory_product", "sku-xyz")
            .Should().NotBe(Identity.InventoryProductRoot("sku-xyz"));
    }

    [Fact]
    public void ToProtoBytes_HexForCustomerRoot_MatchesPerC0114()
    {
        // C-0114: customer_root("alice@x.com") → 16-byte form matches
        // hex "9141d64406025762a8b9d74e7d5a3d45".
        var bytes = Identity.ToProtoBytes(Identity.CustomerRoot("alice@x.com"));
        bytes.Length.Should().Be(16);
        var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        hex.Should().Be("9141d64406025762a8b9d74e7d5a3d45");
    }
}

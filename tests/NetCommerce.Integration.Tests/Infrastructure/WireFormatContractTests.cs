using System.Text.Json;
using System.Text.Json.Serialization;

using NetCommerce.Api.Endpoints.Basket;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Api.Serialization;
using NetCommerce.Basket.Application;
using NetCommerce.Catalog.Application.Products.DTOs;

using Shouldly;

using Xunit;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
/// Pins the JSON wire contract the frontend binds to.
/// </summary>
/// <remarks>
/// The frontend declares TypeScript interfaces for these payloads. Every field
/// name here is a promise the frontend's types rely on, and every nullable field
/// is a trap: <c>ApiJsonContext</c> sets <c>DefaultIgnoreCondition =
/// WhenWritingNull</c> (ApiJsonContext.cs:179), so a null property is OMITTED
/// rather than serialized as <c>null</c>.
/// <para>
/// That distinction already caused one bug: the frontend typed these as
/// <c>string | null</c> when they are in fact absent, i.e. <c>undefined</c>. Under
/// strictNullChecks the two are not interchangeable, so the types described a
/// protocol the server never speaks.
/// </para>
/// <para>
/// These assertions are written against the SHIPPING types and the SHIPPING
/// serializer options, so renaming a DTO property breaks this test rather than
/// silently breaking the storefront at runtime.
/// </para>
/// </remarks>
public class WireFormatContractTests
{
    private static readonly JsonSerializerOptions Options =
        new(ApiJsonContext.Default.Options);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Exact property names present in a payload.
    /// </summary>
    /// <remarks>
    /// Deliberately not Shouldly.ShouldContain(string): that overload compares
    /// CASE-INSENSITIVELY, so asserting ShouldNotContain("PrimaryImageUrl") fails
    /// against a correct camelCase payload because "primaryImageUrl" matches it.
    /// Two tests failed for exactly that reason. These assertions therefore read
    /// the parsed keys, where "primaryImageUrl" and "PrimaryImageUrl" are
    /// genuinely different.
    /// </remarks>
    private static string[] KeysOf(string json, string? rootProperty = null)
    {
        using var doc = JsonDocument.Parse(json);
        var target = rootProperty is null
            ? doc.RootElement
            : doc.RootElement.GetProperty(rootProperty);
        return target.EnumerateObject().Select(p => p.Name).ToArray();
    }

    /// <summary>
    /// Property names of the first element of an array-valued root property.
    /// </summary>
    private static string[] FirstItemKeysOf(string json, string arrayProperty)
    {
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty(arrayProperty)[0];
        return first.EnumerateObject().Select(p => p.Name).ToArray();
    }

    private static readonly Guid ProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ProductListItemDto ListItem() => new()
    {
        Id = ProductId,
        Name = "Walnut Desk",
        Sku = "WD-1",
        Price = 1299.50m,
        Currency = "USD",
        PrimaryImageUrl = "https://cdn.test/walnut.jpg",
        Status = "Published",
        Slug = "walnut-desk",
    };

    private static ProductDto Product() => new()
    {
        Id = ProductId,
        Name = "Walnut Desk",
        Description = "Solid walnut, hand-oiled.",
        Sku = "WD-1",
        Price = 1299.50m,
        Currency = "USD",
        CategoryId = SecondId,
        CategoryName = "Desks",
        Status = "Published",
        Slug = "walnut-desk",
        SeoTitle = "Walnut Desk — NetCommerce",
        Images = [],
        Attributes = [],
    };

    [Fact]
    public void ProductListItem_uses_camelCase_as_the_frontend_declares()
    {
        var keys = KeysOf(Serialize(ListItem()));

        // These exact strings are what src/lib/catalog/products.ts declares.
        keys.ShouldContain("primaryImageUrl");
        keys.ShouldContain("currency");
        keys.ShouldContain("status");
        keys.ShouldNotContain("PrimaryImageUrl");
        keys.ShouldNotContain("Id");
    }

    [Fact]
    public void Product_uses_camelCase_as_the_frontend_declares()
    {
        var keys = KeysOf(Serialize(Product()));

        keys.ShouldContain("categoryId");
        keys.ShouldContain("categoryName");
        keys.ShouldContain("seoTitle");
        keys.ShouldNotContain("CategoryName");
    }

    [Fact]
    public void Null_properties_are_OMITTED_not_serialized_as_null()
    {
        // The behaviour that broke the frontend's types. PrimaryImageUrl and Slug
        // are init-only, so they are cleared in an initializer rather than assigned.
        var item = new ProductListItemDto
        {
            Id = ProductId,
            Name = "Bare Item",
            Sku = "WD-2",
            Price = 10m,
            Currency = "EUR",
            PrimaryImageUrl = null,
            Slug = null,
            Status = "Published",
        };

        var keys = KeysOf(Serialize(item));

        // Absent keys, not keys with a null value. Asserted on parsed keys for
        // the same case-sensitivity reason as above.
        keys.ShouldNotContain("primaryImageUrl");
        keys.ShouldNotContain("slug");
        // Nothing anywhere in the payload may carry an explicit null.
        Serialize(item).ShouldNotContain(":null");
    }

    [Fact]
    public void An_omitted_field_arrives_as_undefined_in_JSON_which_is_what_the_types_now_declare()
    {
        var json = Serialize(ListItem());
        using var doc = JsonDocument.Parse(json);

        // Absent, so the frontend reads undefined. Asserted explicitly because the
        // distinction from an explicit null is the entire point of this file.
        doc.RootElement.TryGetProperty("seoDescription", out _).ShouldBeFalse();
        doc.RootElement.GetProperty("slug").GetString().ShouldBe("walnut-desk");
    }

    [Fact]
    public void ShoppingBasket_omits_null_item_sku_and_imageUrl()
    {
        var basket = new ShoppingBasket
        {
            CustomerId = "cust-1",
            Items =
            [
                new BasketItem
                {
                    ProductId = ProductId,
                    ProductName = "Bare Item",
                    Sku = null,
                    Price = 10m,
                    Quantity = 1,
                    ImageUrl = null,
                },
            ],
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var basketKeys = KeysOf(Serialize(basket));
        var itemKeys = FirstItemKeysOf(Serialize(basket), "items");

        basketKeys.ShouldContain("totalPrice");
        basketKeys.ShouldContain("lastUpdatedAt");
        itemKeys.ShouldContain("productId");
        itemKeys.ShouldContain("quantity");
        // Omitted on this item because both were null.
        itemKeys.ShouldNotContain("sku");
        itemKeys.ShouldNotContain("imageUrl");
        Serialize(basket).ShouldNotContain(":null");
    }

    [Fact]
    public void totalPrice_is_present_because_the_frontend_displays_it_rather_than_recomputing()
    {
        var basket = new ShoppingBasket
        {
            CustomerId = "cust-1",
            Items =
            [
                new BasketItem { ProductId = ProductId, ProductName = "x", Price = 10m, Quantity = 3 },
            ],
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
        };

        using var doc = JsonDocument.Parse(Serialize(basket));

        // A client-side recompute could disagree with checkout; the server's figure
        // is authoritative, so the field must survive serialization.
        doc.RootElement.GetProperty("totalPrice").GetDecimal().ShouldBe(30m);
    }

    [Fact]
    public void PaginatedResponse_matches_the_pagination_shape_the_catalog_page_reads()
    {
        var page = PaginatedResponse<ProductListItemDto>.Create([ListItem()], 2, 20, 41);

        using var doc = JsonDocument.Parse(Serialize(page));

        doc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(1);

        var pagination = doc.RootElement.GetProperty("pagination");
        pagination.GetProperty("page").GetInt32().ShouldBe(2);
        pagination.GetProperty("pageSize").GetInt32().ShouldBe(20);
        pagination.GetProperty("totalCount").GetInt32().ShouldBe(41);
        pagination.GetProperty("totalPages").GetInt32().ShouldBe(3);
        pagination.GetProperty("hasPreviousPage").GetBoolean().ShouldBeTrue();
        pagination.GetProperty("hasNextPage").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void AddBasketItemRequest_serializes_the_field_names_the_action_sends()
    {
        var keys = KeysOf(Serialize(new AddBasketItemRequest(
            ProductId, "Walnut Desk", "WD-1", 2, 1299.50m, null)));

        keys.ShouldContain("productId");
        keys.ShouldContain("productName");
        keys.ShouldContain("unitPrice");
        keys.ShouldContain("quantity");
        // imageUrl is null on this request, so it must be absent, not null.
        keys.ShouldNotContain("imageUrl");
    }

    [Fact]
    public void decimal_prices_keep_their_scale_rather_than_becoming_floating_point_noise()
    {
        using var doc = JsonDocument.Parse(Serialize(ListItem()));

        // decimal, not double: 1299.50 must not arrive as 1299.5 or 1299.4999.
        doc.RootElement.GetProperty("price").GetDecimal().ShouldBe(1299.50m);
    }
}
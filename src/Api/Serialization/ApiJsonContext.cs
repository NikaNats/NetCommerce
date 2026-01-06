using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Categories.Queries;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Inventory.Application.Stock.Queries;
using Microsoft.Extensions.Hosting;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Payments.Application.Transactions.Commands;

namespace NetCommerce.Api.Serialization;

// Core ASP.NET Core types
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]

// Core ASP.NET Core types
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]

// Catalog DTOs and responses
[JsonSerializable(typeof(ProductDto))]
[JsonSerializable(typeof(ProductImageDto))]
[JsonSerializable(typeof(ProductAttributeDto))]
[JsonSerializable(typeof(ProductListItemDto))]
[JsonSerializable(typeof(CategoryDto))]

// Inventory DTOs and commands
[JsonSerializable(typeof(StockDto))]

// Ordering DTOs and commands
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(AddOrderItemCommand))]
[JsonSerializable(typeof(CancelOrderCommand))]
[JsonSerializable(typeof(AddressDto))]

// Payments commands
[JsonSerializable(typeof(RefundPaymentTransactionCommand))]

// Finance commands
[JsonSerializable(typeof(CheckDailyReconciliation))]

// Common response types
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(HealthEntry[]))]

// Query types (for potential serialization in responses)
[JsonSerializable(typeof(GetProductByIdQuery))]
[JsonSerializable(typeof(GetProductBySlugQuery))]
[JsonSerializable(typeof(GetAllCategoriesQuery))]
[JsonSerializable(typeof(GetCategoryByIdQuery))]
[JsonSerializable(typeof(GetCategoryBySlugQuery))]
[JsonSerializable(typeof(GetChildCategoriesQuery))]
[JsonSerializable(typeof(GetRootCategoriesQuery))]
[JsonSerializable(typeof(GetStockByProductIdQuery))]
[JsonSerializable(typeof(GetLowStockItemsQuery))]
[JsonSerializable(typeof(GetStockBySkuQuery))]

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ApiJsonContext : JsonSerializerContext
{
}

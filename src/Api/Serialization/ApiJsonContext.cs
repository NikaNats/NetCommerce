using System.Text.Json.Serialization;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Kernel.Core.Application;
using NetCommerce.Api.Endpoints.Common;

// Modules
using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Basket.Application;
using NetCommerce.Inventory.Application.Stock.Queries;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Media.Application.Services;
using NetCommerce.Api.Endpoints.Ordering;
using NetCommerce.Api.Endpoints.Basket;
using NetCommerce.Api.Endpoints.Inventory;
using NetCommerce.Api.Endpoints.Admin;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Finance.Application.Commands;

namespace NetCommerce.Api.Serialization;

// ============================================================================
// SYSTEM TYPES
// ============================================================================
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]

// ============================================================================
// KERNEL & INFRASTRUCTURE
// ============================================================================
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]

// ============================================================================
// GENERIC WRAPPERS (CRITICAL FOR AOT)
// ============================================================================
// You MUST register the specific generic variants you return.
[JsonSerializable(typeof(Result<Guid>))]
[JsonSerializable(typeof(Result<string>))]
[JsonSerializable(typeof(Result<bool>))]

// Catalog Generics
[JsonSerializable(typeof(Result<ProductDto>))]
[JsonSerializable(typeof(Result<CategoryDto>))]
[JsonSerializable(typeof(Result<IReadOnlyList<CategoryDto>>))]
[JsonSerializable(typeof(List<CategoryDto>))]
[JsonSerializable(typeof(Result<PagedResult<ProductDto>>))]
[JsonSerializable(typeof(PagedResult<ProductDto>))]
[JsonSerializable(typeof(PaginatedResponse<ProductDto>))]
[JsonSerializable(typeof(PaginationMetadata))]

// Inventory Generics
[JsonSerializable(typeof(Result<StockDto>))]
[JsonSerializable(typeof(Result<IReadOnlyList<StockDto>>))]
[JsonSerializable(typeof(List<StockDto>))]

// Media Generics
[JsonSerializable(typeof(Result<PresignedUploadUrl>))]

// Ordering Generics
[JsonSerializable(typeof(Result<List<StuckSagaDto>>))]
[JsonSerializable(typeof(List<StuckSagaDto>))]

// ============================================================================
// DOMAIN DTOS
// ============================================================================

// Catalog
[JsonSerializable(typeof(CreateProductCommand))]
[JsonSerializable(typeof(UpdateProductCommand))]
[JsonSerializable(typeof(ProductDto))]
[JsonSerializable(typeof(ProductImageDto))]
[JsonSerializable(typeof(ProductAttributeDto))]
[JsonSerializable(typeof(ProductListItemDto))]
[JsonSerializable(typeof(CategoryDto))]

// Basket
[JsonSerializable(typeof(ShoppingBasket))]
[JsonSerializable(typeof(BasketItem))]
[JsonSerializable(typeof(AddBasketItemRequest))]
[JsonSerializable(typeof(UpdateQuantityRequest))]

// Ordering
[JsonSerializable(typeof(CreateOrderCommand))]
[JsonSerializable(typeof(OrderItemRequest))]
[JsonSerializable(typeof(AddressDto))]
[JsonSerializable(typeof(StuckSagasResponse))]
[JsonSerializable(typeof(StuckSagaDto))]
[JsonSerializable(typeof(OrderFulfillmentSaga))] // For Wolverine saga serialization

// Inventory
[JsonSerializable(typeof(StockDto))]
[JsonSerializable(typeof(UpdateStockQuantityRequest))]

// Media
[JsonSerializable(typeof(PresignedUploadUrl))]

// Payments
[JsonSerializable(typeof(RefundPaymentTransactionCommand))]

// Finance
[JsonSerializable(typeof(CheckDailyReconciliation))]

// Admin - Order Recovery
[JsonSerializable(typeof(ForceCompleteSagaRequest))]
[JsonSerializable(typeof(OverridePaymentStatusRequest))]
[JsonSerializable(typeof(ForceCancelOrderRequest))]
[JsonSerializable(typeof(RetryStepRequest))]
[JsonSerializable(typeof(BulkRetryRequest))]
[JsonSerializable(typeof(ForceCompleteOrderSagaCommand))]
[JsonSerializable(typeof(OverridePaymentStatusCommand))]
[JsonSerializable(typeof(ForceCancelOrderCommand))]
[JsonSerializable(typeof(RetrySagaStepCommand))]
[JsonSerializable(typeof(BulkRetrySagasCommand))]

// Admin - Finance
[JsonSerializable(typeof(TriggerReconciliationRequest))]
[JsonSerializable(typeof(ResolveDiscrepancyRequest))]

// Common response types
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(HealthEntry[]))]

// Rate limiting
[JsonSerializable(typeof(RateLimitResponse))]

// Exception handling (ProblemDetails already registered above)
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails))]

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
internal partial class ApiJsonContext : JsonSerializerContext
{
}

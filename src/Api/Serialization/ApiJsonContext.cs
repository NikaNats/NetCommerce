#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Kernel.Application;
using NetCommerce.Api.Endpoints.Common;

// Modules & Endpoints
using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Categories.Commands;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Basket.Application;
using NetCommerce.Inventory.Application.Stock.Queries;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Media.Application.Services;
using NetCommerce.Api.Endpoints.Catalog;
using NetCommerce.Api.Endpoints.Ordering;
using NetCommerce.Api.Endpoints.Basket;
using NetCommerce.Api.Endpoints.Inventory;
using NetCommerce.Api.Endpoints.Media;
using NetCommerce.Api.Endpoints.Admin;
using NetCommerce.Api.Endpoints.Auth;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Finance.Application.Commands;

namespace NetCommerce.Api.Serialization;

// ============================================================================
// SYSTEM & PRIMITIVE TYPES
// ============================================================================
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, long>))]

// ============================================================================
// KERNEL & INFRASTRUCTURE
// ============================================================================
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]

// ============================================================================
// GENERIC WRAPPERS (CRITICAL FOR AOT RESULT PATTERN)
// ============================================================================
[JsonSerializable(typeof(Result))]
[JsonSerializable(typeof(Result<Guid>))]
[JsonSerializable(typeof(Result<string>))]
[JsonSerializable(typeof(Result<bool>))]
[JsonSerializable(typeof(Result<int>))]

// Catalog Generics
[JsonSerializable(typeof(Result<ProductDto>))]
[JsonSerializable(typeof(Result<CategoryDto>))]
[JsonSerializable(typeof(Result<IReadOnlyList<CategoryDto>>))]
[JsonSerializable(typeof(List<CategoryDto>))]
[JsonSerializable(typeof(CategoryDto[]))]
[JsonSerializable(typeof(Result<PagedResult<ProductDto>>))]
[JsonSerializable(typeof(PagedResult<ProductDto>))]
[JsonSerializable(typeof(PaginatedResponse<ProductDto>))]
[JsonSerializable(typeof(PaginatedResponse<ProductListItemDto>))]
[JsonSerializable(typeof(PaginationMetadata))]

// Inventory Generics
[JsonSerializable(typeof(Result<StockDto>))]
[JsonSerializable(typeof(Result<IReadOnlyList<StockDto>>))]
[JsonSerializable(typeof(List<StockDto>))]
[JsonSerializable(typeof(StockDto[]))]

// Media Generics
[JsonSerializable(typeof(Result<PresignedUploadUrl>))]

// Ordering Generics
[JsonSerializable(typeof(Result<List<StuckSagaDto>>))]
[JsonSerializable(typeof(List<StuckSagaDto>))]

// ============================================================================
// DOMAIN DTOS & ENDPOINT REQUESTS / RESPONSES
// ============================================================================

// Catalog & Search
[JsonSerializable(typeof(CreateProductCommand))]
[JsonSerializable(typeof(UpdateProductCommand))]
[JsonSerializable(typeof(UpdateProductPriceRequest))]
[JsonSerializable(typeof(AddProductImageRequest))]
[JsonSerializable(typeof(CreateCategoryCommand))]
[JsonSerializable(typeof(UpdateCategoryCommand))]
[JsonSerializable(typeof(ProductDto))]
[JsonSerializable(typeof(ProductImageDto))]
[JsonSerializable(typeof(ProductAttributeDto))]
[JsonSerializable(typeof(ProductListItemDto))]
[JsonSerializable(typeof(CategoryDto))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ProductSearchResult))]
[JsonSerializable(typeof(ProductSearchResult[]))]

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
[JsonSerializable(typeof(OrderFulfillmentSaga))]

// Inventory
[JsonSerializable(typeof(CreateStockCommand))]
[JsonSerializable(typeof(ReserveStockCommand))]
[JsonSerializable(typeof(StockDto))]
[JsonSerializable(typeof(UpdateStockQuantityRequest))]

// Media (Direct Upload & Presigned)
[JsonSerializable(typeof(PresignedUploadUrl))]
[JsonSerializable(typeof(UploadMediaResponse))]

// Payments
[JsonSerializable(typeof(RefundPaymentTransactionCommand))]

// Finance
[JsonSerializable(typeof(CheckDailyReconciliation))]
[JsonSerializable(typeof(NetCommerce.Finance.Domain.Reconciliation.ReconciliationSession))]

// Admin DLQ Endpoints
[JsonSerializable(typeof(BulkReplayDlqRequest))]
[JsonSerializable(typeof(DlqListResponse))]
[JsonSerializable(typeof(DlqEnvelopeDto))]
[JsonSerializable(typeof(BulkReplayResponse))]

// Admin Finance Endpoints
[JsonSerializable(typeof(TriggerReconciliationRequest))]
[JsonSerializable(typeof(ResolveDiscrepancyRequest))]

// Admin Order Recovery Endpoints
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

// Auth - BFF Keycloak proxy
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(RevokeRequest))]
[JsonSerializable(typeof(LogoutRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(SessionInfoResponse))]
[JsonSerializable(typeof(AuthErrorResponse))]

// Rate Limiting
[JsonSerializable(typeof(RateLimitResponse))]

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default
)]
internal partial class ApiJsonContext : JsonSerializerContext
{
}

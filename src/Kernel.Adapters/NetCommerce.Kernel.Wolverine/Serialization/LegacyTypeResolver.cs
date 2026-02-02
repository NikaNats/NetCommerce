#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;

namespace NetCommerce.Kernel.Wolverine.Serialization;

/// <summary>
///     JSON type resolver for backward compatibility with legacy SharedKernel namespace.
///
///     <para>
///     CRITICAL FOR PRODUCTION: After the Phase 5/6 namespace migration, Wolverine's
///     saga state and outbox messages contain fully qualified type names. This resolver
///     ensures that existing in-flight sagas and pending messages can be deserialized
///     after deployment.
///     </para>
///
///     <para>
///     Migration Timeline:
///     1. Deploy with this resolver enabled
///     2. Wait for all active sagas to complete (typically 30-60 days for order lifecycle)
///     3. Monitor "LegacyTypeResolution" metric for zero hits
///     4. Remove this resolver in a future release
///     </para>
/// </summary>
public sealed class LegacyTypeResolver : DefaultJsonTypeInfoResolver
{
    /// <summary>
    ///     Maps legacy SharedKernel type names to canonical Domain.Shared types.
    ///     These mappings handle the Phase 5/6 namespace migration.
    /// </summary>
    private static readonly Dictionary<string, Type> LegacyTypeMappings = new(StringComparer.Ordinal)
    {
        // Value Objects
        ["NetCommerce.SharedKernel.Domain.Money"] = typeof(Money),
        ["NetCommerce.SharedKernel.Domain.Money, NetCommerce.SharedKernel"] = typeof(Money),
        ["NetCommerce.SharedKernel.Domain.PriceBreakdown"] = typeof(PriceBreakdown),
        ["NetCommerce.SharedKernel.Domain.PriceBreakdown, NetCommerce.SharedKernel"] = typeof(PriceBreakdown),

        // Integration Events - Order Lifecycle
        ["NetCommerce.SharedKernel.Events.StartOrderFulfillmentCommand"] = typeof(StartOrderFulfillmentCommand),
        ["NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent"] = typeof(OrderSubmittedIntegrationEvent),
        ["NetCommerce.SharedKernel.Events.OrderGracePeriodConfirmedIntegrationEvent"] = typeof(OrderGracePeriodConfirmedIntegrationEvent),
        ["NetCommerce.SharedKernel.Events.OrderPlacedIntegrationEvent"] = typeof(OrderPlacedIntegrationEvent),
        ["NetCommerce.SharedKernel.Events.OrderCancelledIntegrationEvent"] = typeof(OrderCancelledIntegrationEvent),

        // Integration Events - Inventory
        ["NetCommerce.SharedKernel.Events.ReserveInventoryCommand"] = typeof(ReserveInventoryCommand),
        ["NetCommerce.SharedKernel.Events.InventoryReserved"] = typeof(InventoryReserved),
        ["NetCommerce.SharedKernel.Events.InventoryReservationFailed"] = typeof(InventoryReservationFailed),
        ["NetCommerce.SharedKernel.Events.LockInventoryForPaymentCommand"] = typeof(LockInventoryForPaymentCommand),
        ["NetCommerce.SharedKernel.Events.InventoryLocked"] = typeof(InventoryLocked),
        ["NetCommerce.SharedKernel.Events.ConfirmInventoryCommand"] = typeof(ConfirmInventoryCommand),
        ["NetCommerce.SharedKernel.Events.InventoryConfirmed"] = typeof(InventoryConfirmed),
        ["NetCommerce.SharedKernel.Events.InventoryConfirmationFailed"] = typeof(InventoryConfirmationFailed),
        ["NetCommerce.SharedKernel.Events.ReleaseInventoryReservationCommand"] = typeof(ReleaseInventoryReservationCommand),

        // Integration Events - Payments
        ["NetCommerce.SharedKernel.Events.RequestPaymentCommand"] = typeof(RequestPaymentCommand),
        ["NetCommerce.SharedKernel.Events.PaymentInitiated"] = typeof(PaymentInitiated),
        ["NetCommerce.SharedKernel.Events.PaymentSucceeded"] = typeof(PaymentSucceeded),
        ["NetCommerce.SharedKernel.Events.PaymentFailed"] = typeof(PaymentFailed),
        ["NetCommerce.SharedKernel.Events.RefundPaymentCommand"] = typeof(RefundPaymentCommand),
        ["NetCommerce.SharedKernel.Events.RefundCompleted"] = typeof(RefundCompleted),
        ["NetCommerce.SharedKernel.Events.RefundFailed"] = typeof(RefundFailed),

        // Saga Messages
        ["NetCommerce.SharedKernel.Events.OrderItemReservation"] = typeof(OrderItemReservation),
        ["NetCommerce.SharedKernel.Events.ReservedItem"] = typeof(ReservedItem),
        ["NetCommerce.SharedKernel.Events.InventoryReservationTimeoutMessage"] = typeof(InventoryReservationTimeoutMessage),
        ["NetCommerce.SharedKernel.Events.GracePeriodTimeout"] = typeof(GracePeriodTimeout),
        ["NetCommerce.SharedKernel.Events.PaymentTimeoutMessage"] = typeof(PaymentTimeoutMessage),
        ["NetCommerce.SharedKernel.Events.InventoryConfirmationTimeoutMessage"] = typeof(InventoryConfirmationTimeoutMessage),
        ["NetCommerce.SharedKernel.Events.FinalizeOrderCommand"] = typeof(FinalizeOrderCommand),
        ["NetCommerce.SharedKernel.Events.FailOrderCommand"] = typeof(FailOrderCommand),
    };

    /// <summary>
    ///     Counter for monitoring legacy type resolutions.
    ///     When this drops to zero over a sustained period, the resolver can be removed.
    /// </summary>
    private static long _legacyResolutionCount;

    /// <summary>
    ///     Gets the count of legacy type resolutions since application start.
    /// </summary>
    public static long LegacyResolutionCount => Interlocked.Read(ref _legacyResolutionCount);

    /// <inheritdoc />
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);

        // NOTE: Do NOT add polymorphism options here. Sealed record types like OrderItemReservation
        // and ReservedItem cannot support polymorphism, and adding PolymorphismOptions causes:
        // "Specified type does not support polymorphism. Polymorphic types cannot be structs,
        //  sealed types, generic types or System.Object."
        //
        // The LegacyTypeConverter handles $type discriminators for Money and PriceBreakdown,
        // and other types don't need polymorphic handling.

        return typeInfo;
    }

    /// <summary>
    ///     Resolves a legacy type name to its canonical type.
    ///     Called by Wolverine during deserialization when it encounters
    ///     a $type discriminator with an old namespace.
    /// </summary>
    /// <param name="legacyTypeName">The fully qualified type name from the JSON.</param>
    /// <returns>The resolved type, or null if not a legacy type.</returns>
    public static Type? ResolveLegacyType(string legacyTypeName)
    {
        if (string.IsNullOrEmpty(legacyTypeName))
            return null;

        // Try exact match first
        if (LegacyTypeMappings.TryGetValue(legacyTypeName, out var type))
        {
            Interlocked.Increment(ref _legacyResolutionCount);
            return type;
        }

        // Try prefix match for assembly-qualified names
        foreach (var mapping in LegacyTypeMappings)
        {
            if (legacyTypeName.StartsWith(mapping.Key, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _legacyResolutionCount);
                return mapping.Value;
            }
        }

        return null;
    }

    /// <summary>
    ///     Creates JSON serializer options configured with the legacy type resolver.
    /// </summary>
    public static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            TypeInfoResolver = new LegacyTypeResolver(),
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new LegacyTypeConverter()
            }
        };
    }

}

/// <summary>
///     JSON converter that handles legacy type discriminators in Wolverine saga state.
/// </summary>
public sealed class LegacyTypeConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        // Handle Money and PriceBreakdown which might have legacy $type discriminators
        return typeToConvert == typeof(Money) ||
               typeToConvert == typeof(PriceBreakdown);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(Money))
            return new MoneyConverter();
        if (typeToConvert == typeof(PriceBreakdown))
            return new PriceBreakdownConverter();
        return null;
    }

    private sealed class MoneyConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                decimal amount = 0;
                string currency = "GEL";

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propertyName = reader.GetString()?.ToLowerInvariant();
                        reader.Read();

                        switch (propertyName)
                        {
                            case "amount":
                                amount = reader.GetDecimal();
                                break;
                            case "currency":
                                currency = reader.GetString() ?? "GEL";
                                break;
                            case "$type":
                                // Skip legacy type discriminator
                                _ = reader.GetString();
                                break;
                        }
                    }
                }

                return Money.Create(amount, currency);
            }

            throw new JsonException("Expected StartObject for Money");
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("amount", value.Amount);
            writer.WriteString("currency", value.Currency);
            writer.WriteEndObject();
        }
    }

    private sealed class PriceBreakdownConverter : JsonConverter<PriceBreakdown>
    {
        public override PriceBreakdown Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                decimal basePrice = 0, quantity = 0, discountAmount = 0, taxAmount = 0, taxRate = 0;
                string taxType = "NONE", currency = "GEL";

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        break;

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propertyName = reader.GetString()?.ToLowerInvariant();
                        reader.Read();

                        switch (propertyName)
                        {
                            case "baseprice":
                                basePrice = reader.GetDecimal();
                                break;
                            case "quantity":
                                quantity = reader.GetDecimal();
                                break;
                            case "discountamount":
                                discountAmount = reader.GetDecimal();
                                break;
                            case "taxamount":
                                taxAmount = reader.GetDecimal();
                                break;
                            case "taxrate":
                                taxRate = reader.GetDecimal();
                                break;
                            case "taxtype":
                                taxType = reader.GetString() ?? "NONE";
                                break;
                            case "currency":
                                currency = reader.GetString() ?? "GEL";
                                break;
                            case "$type":
                                // Skip legacy type discriminator
                                _ = reader.GetString();
                                break;
                        }
                    }
                }

                return PriceBreakdown.CreateFromLineTotals(
                    basePrice, (int)quantity, discountAmount, taxAmount, taxRate, taxType, currency);
            }

            throw new JsonException("Expected StartObject for PriceBreakdown");
        }

        public override void Write(Utf8JsonWriter writer, PriceBreakdown value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("basePrice", value.BasePrice);
            writer.WriteNumber("quantity", value.Quantity);
            writer.WriteNumber("discountAmount", value.DiscountAmount);
            writer.WriteNumber("taxAmount", value.TaxAmount);
            writer.WriteNumber("taxRate", value.TaxRate);
            writer.WriteString("taxType", value.TaxType);
            writer.WriteString("currency", value.Currency);
            writer.WriteEndObject();
        }
    }
}

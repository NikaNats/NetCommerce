#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using NetCommerce.Kernel.Core.Ids;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Bulk configuration for Strongly Typed IDs.
///     Automatically detects any IStronglyTypedId&lt;T&gt; and applies the converter.
/// </summary>
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "StronglyTypedId convention uses MakeGenericType at startup only. All ID types are statically referenced by DbContext configurations.")]
public class StronglyTypedIdConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var type = property.ClrType;

                // Identification: Check for IStronglyTypedId<T>
                // We identify types that implement the generic interface
                var idInterface = type.GetInterfaces()
                    .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>));

                if (idInterface is not null)
                {
                    // Dynamically create the generic converter: StronglyTypedIdValueConverter<OrderId>
                    var converterType = typeof(StronglyTypedIdValueConverter<>).MakeGenericType(type);

                    // Activator is fine here; it runs once per app startup.
                    var converter = (ValueConverter)Activator.CreateInstance(converterType)!;

                    property.SetValueConverter(converter);
                }
            }
        }
    }
}

/// <summary>
///     Generic EF Core Value Converter.
///     Builds the Expression Tree manually to bypass CS8927 limitation.
/// </summary>
[UnconditionalSuppressMessage("AOT", "IL2060:MakeGenericMethod",
    Justification = "TId is constrained to IStronglyTypedId<TId> which guarantees the Create method exists.")]
[UnconditionalSuppressMessage("AOT", "IL2070:DynamicallyAccessedMembers",
    Justification = "TId types are statically referenced by EF Core entity configurations.")]
public class StronglyTypedIdValueConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, IStronglyTypedId<TId>
{
    public StronglyTypedIdValueConverter()
        : base(
            // To Database: simple lambda, supported natively
            id => id.Value,
            // From Database: Manual Expression Tree to call static abstract 'Create'
            CreateConverterExpression(),
            // Mapping Hints: For optimal parameterization
            new ConverterMappingHints(valueGeneratorFactory: (p, t) => new StronglyTypedIdValueGenerator<TId>())
        )
    {
    }

    // Solves CS8927: Manually builds 'v => TId.Create(v)'
    // The C# compiler cannot yet generate Expression Trees for static abstract members,
    // so we do it "by hand" using Reflection once at startup.
    private static Expression<Func<Guid, TId>> CreateConverterExpression()
    {
        var param = Expression.Parameter(typeof(Guid), "v");

        // Find TId.Create(Guid)
        var createMethod = typeof(TId).GetMethod(
            nameof(IStronglyTypedId<TId>.Create),
            BindingFlags.Static | BindingFlags.Public,
            new[] { typeof(Guid) }
        );

        if (createMethod is null)
        {
            throw new InvalidOperationException($"Static method 'Create' not found on type {typeof(TId).Name}");
        }

        var call = Expression.Call(createMethod, param);
        return Expression.Lambda<Func<Guid, TId>>(call, param);
    }
}

/// <summary>
///     Value Generator using Cached Delegate to ensure high performance.
/// </summary>
[UnconditionalSuppressMessage("AOT", "IL2070:DynamicallyAccessedMembers",
    Justification = "TId types are statically referenced by EF Core entity configurations.")]
public class StronglyTypedIdValueGenerator<TId> : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    // Cache the delegate to ensure high performance (Static Constructor pattern)
    private static readonly Func<TId> _newIdFactory = CreateFactory();

    private static Func<TId> CreateFactory()
    {
        // 1. Try to find a custom implementation of 'New' (e.g. for Sequential GUIDs)
        var method = typeof(TId).GetMethod(
            nameof(IStronglyTypedId<TId>.New),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy
        );

        // 2. If the struct doesn't implement 'New' explicitly (it uses the interface default),
        // reflection might return null. In that case, we fallback to a simple lambda.
        // This lambda is compiled code, not an expression tree, so accessing TId.Create is valid here.
        if (method == null)
        {
             return () => TId.Create(Guid.NewGuid());
        }

        // 3. If a custom method exists, use it.
        return (Func<TId>)Delegate.CreateDelegate(typeof(Func<TId>), method);
    }

    public override bool GeneratesTemporaryValues => false;

    public override TId Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        return _newIdFactory();
    }
}

#nullable enable
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Api.Endpoints;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Serialization;
using NetCommerce.Kernel.Core.Results;
using Shouldly;
using Xunit;

namespace NetCommerce.Architecture.Tests;

/// <summary>
///     Verifies that every Minimal API contract - both endpoint input parameters and the response
///     types declared via fluent .Produces&lt;T&gt;() metadata - is pre-registered in ApiJsonContext.
///     Endpoints are mapped into a real RouteEndpointBuilder graph so the compiled
///     EndpointMetadataCollection is inspected, exactly as ASP.NET Core would see it under Native AOT.
/// </summary>
public sealed class TrimBoundaryTests
{
    [Fact]
    public void ApiJsonContext_MustRegisterAllEndpointInputAndOutputContracts()
    {
        // 1. Discover all types registered in ApiJsonContext via [JsonSerializable]
        var registeredTypes = typeof(ApiJsonContext).GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => d.ConstructorArguments[0].Value as Type)
            .Where(t => t != null)
            .Select(NormalizeType)
            .ToHashSet();

        // 2. Map all endpoints into a real route metadata tree
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddApiVersioning(); // Required by Asp.Versioning's endpoint finalizer (IReportApiVersions, ...)

        // Accessing Endpoints triggers full RequestDelegateFactory inference. Every DI-injected
        // parameter must be recognizable as a service (via IServiceProviderIsService), otherwise
        // inference treats it as a JSON body parameter and throws. Pre-register all injected
        // service candidates found on endpoint handler methods.
        foreach (var serviceType in CollectInjectedServiceCandidates())
        {
            services.AddTransient(serviceType, _ => throw new NotSupportedException(
                $"{serviceType.Name} is registered for metadata inference only and must never be resolved."));
        }

        var sp = services.BuildServiceProvider();

        var routeBuilder = new TestEndpointRouteBuilder(new ApplicationBuilder(sp));
        var versionSet = routeBuilder.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        routeBuilder.MapNetCommerceEndpoints(versionSet);

        var missingTypes = new List<(string Route, Type MissingContract, string Direction)>();

        foreach (var endpoint in routeBuilder.DataSources.SelectMany(ds => ds.Endpoints).OfType<RouteEndpoint>())
        {
            var routePattern = endpoint.RoutePattern.RawText ?? "Unknown";

            // Inspect Input Parameters from MethodInfo
            if (endpoint.Metadata.GetMetadata<MethodInfo>() is { } method)
            {
                foreach (var param in method.GetParameters())
                {
                    // Normalize before filtering so Nullable<T> wrappers (e.g. enum query params) are unwrapped
                    var paramType = NormalizeType(param.ParameterType);
                    if (IsFrameworkServiceType(paramType)) continue;

                    if (!IsTypeRegistered(paramType, registeredTypes))
                    {
                        missingTypes.Add((routePattern, paramType, $"Input (Parameter: {param.Name})"));
                    }
                }
            }

            // Inspect Output Metadata (Produces<T> registered via the fluent endpoint builder)
            var producesMetadata = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>();
            foreach (var produces in producesMetadata)
            {
                if (produces.Type == null || produces.Type == typeof(void)) continue;

                var responseType = NormalizeType(produces.Type);
                if (IsFrameworkServiceType(responseType)) continue;

                if (!IsTypeRegistered(responseType, registeredTypes))
                {
                    missingTypes.Add((routePattern, responseType, $"Output (Produces: {produces.StatusCode})"));
                }
            }
        }

        // 3. Assert zero serialization drift
        if (missingTypes.Count > 0)
        {
            var report = string.Join(Environment.NewLine, missingTypes.Distinct().Select(m =>
                $"[MISSING AOT SERIALIZATION] Route '{m.Route}' -> {m.Direction}: {m.MissingContract.FullName}" +
                $"{Environment.NewLine}  Fix: Add [JsonSerializable(typeof({GetCleanTypeName(m.MissingContract)}))] to ApiJsonContext.cs"));

            throw new Xunit.Sdk.XunitException(
                $"Found {missingTypes.Count} contract type(s) omitted from ApiJsonContext. Native AOT will fail at runtime:{Environment.NewLine}{report}");
        }
    }

    [Fact]
    public void KernelCoreErrors_MustBeRegisteredInJsonContext()
    {
        var registeredTypes = typeof(ApiJsonContext).GetCustomAttributesData()
            .Where(d => d.AttributeType == typeof(JsonSerializableAttribute))
            .Select(d => d.ConstructorArguments[0].Value as Type)
            .Where(t => t != null)
            .ToHashSet();

        registeredTypes.ShouldContain(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails));
        registeredTypes.ShouldContain(typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails));
        registeredTypes.ShouldContain(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails));
        registeredTypes.ShouldContain(typeof(Result));
        registeredTypes.ShouldContain(typeof(Result<Guid>));
    }

    private static bool IsTypeRegistered(Type type, HashSet<Type> registered)
    {
        if (registered.Contains(type)) return true;

        // Check if underlying collection element or generic argument is registered
        if (type.IsGenericType)
        {
            var genericArgs = type.GetGenericArguments();
            return genericArgs.All(arg => IsTypeRegistered(NormalizeType(arg), registered));
        }

        if (type.IsArray)
        {
            return IsTypeRegistered(NormalizeType(type.GetElementType()!), registered);
        }

        return false;
    }

    private static Type NormalizeType(Type? type)
    {
        if (type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return Nullable.GetUnderlyingType(type)!;

        return type!;
    }

    private static bool IsFrameworkServiceType(Type type)
    {
        // object/Type appear only via inherited members or GetType() - never JSON body contracts
        if (type == typeof(object) || type == typeof(Type))
            return true;

        // Enums bind from route/query strings; JSON source-gen handles them without explicit registration
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(decimal))
            return true;

        // Interfaces bound from the DI container (IBasketRepository, IStorageService, ...) are never
        // JSON body contracts - Minimal APIs only deserialize concrete request types.
        if (type.IsInterface) return true;

        // DbContext subclasses are DI-injected persistence services
        if (typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(type)) return true;

        // DI-injected services use conventional service suffixes; DTOs/commands never do
        var typeName = type.Name;
        if (typeName.EndsWith("DbContext") || typeName.EndsWith("Repository") ||
            typeName.EndsWith("Client") || typeName.EndsWith("Proxy") ||
            typeName.EndsWith("Service") || typeName.StartsWith("ILogger"))
            return true;

        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("Microsoft.AspNetCore")
            || ns.StartsWith("Microsoft.Extensions")
            || ns.StartsWith("Meilisearch")
            || ns.StartsWith("Wolverine")
            // ClaimsPrincipal / auth principals are injected by the auth middleware, not deserialized.
            || ns.StartsWith("System.Security.Claims")
            // Keycloak token proxy plumbing (KeycloakTokenProxy, KeycloakTokenResult) is internal
            // auth infrastructure injected via DI; it never crosses the HTTP JSON boundary.
            || ns.StartsWith("NetCommerce.Kernel.Security.Authentication")
            || typeof(CancellationToken).IsAssignableFrom(type)
            || typeof(HttpContext).IsAssignableFrom(type)
            || typeof(HttpRequest).IsAssignableFrom(type)
            || typeof(HttpResponse).IsAssignableFrom(type)
            || typeof(ClaimsPrincipal).IsAssignableFrom(type);
    }

    private static string GetCleanTypeName(Type t)
    {
        if (!t.IsGenericType) return t.FullName ?? t.Name;
        var genericDef = t.GetGenericTypeDefinition().FullName!;
        var cleanName = genericDef.Substring(0, genericDef.IndexOf('`'));
        var args = string.Join(", ", t.GetGenericArguments().Select(GetCleanTypeName));
        return $"{cleanName}<{args}>";
    }

    /// <summary>
    ///     Collects every DI-injected service type used as an endpoint handler parameter so the
    ///     test's service provider can report them via IServiceProviderIsService during
    ///     RequestDelegateFactory metadata inference.
    /// </summary>
    private static IEnumerable<Type> CollectInjectedServiceCandidates()
    {
        var apiAssembly = typeof(ApiJsonContext).Assembly;
        var handlerTypes = apiAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IEndpointGroup).IsAssignableFrom(t))
            .Append(apiAssembly.GetType("NetCommerce.Api.Endpoints.Payments.PaymentWebhookEndpoints"))
            .Where(t => t != null)
            .Cast<Type>();

        foreach (var type in handlerTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.DeclaringType != typeof(object));

            foreach (var method in methods)
            {
                foreach (var param in method.GetParameters())
                {
                    if (IsInjectedServiceCandidate(param.ParameterType))
                        yield return param.ParameterType;
                }
            }
        }
    }

    /// <summary>
    ///     Injected-service candidates: types RequestDelegateFactory must treat as services rather
    ///     than JSON body contracts. Excludes primitives and the handler context types ASP.NET Core
    ///     recognizes natively (HttpContext, ClaimsPrincipal, CancellationToken, ...).
    /// </summary>
    private static bool IsInjectedServiceCandidate(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(decimal))
            return false;

        if (type == typeof(CancellationToken) || typeof(HttpContext).IsAssignableFrom(type) ||
            typeof(ClaimsPrincipal).IsAssignableFrom(type))
            return false;

        if (type.IsInterface) return true;
        if (typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(type)) return true;

        var typeName = type.Name;
        if (typeName.EndsWith("DbContext") || typeName.EndsWith("Repository") ||
            typeName.EndsWith("Client") || typeName.EndsWith("Proxy"))
            return true;

        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("Wolverine")
            || ns.StartsWith("Meilisearch")
            || ns.StartsWith("NetCommerce.Kernel.Security.Authentication");
    }

    /// <summary>
    ///     Minimal IEndpointRouteBuilder that collects EndpointDataSources without a live
    ///     WebApplication - endpoint mapping only needs a service provider and an
    ///     application builder factory, never a running server.
    /// </summary>
    private sealed class TestEndpointRouteBuilder(IApplicationBuilder applicationBuilder) : IEndpointRouteBuilder
    {
        public IApplicationBuilder CreateApplicationBuilder() => applicationBuilder.New();
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IServiceProvider ServiceProvider => applicationBuilder.ApplicationServices;
    }
}

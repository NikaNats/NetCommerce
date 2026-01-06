namespace NetCommerce.Kernel.SourceGenerators;

/// <summary>
///     Attribute to mark record structs as strongly typed IDs for source generation.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false)]
public class StronglyTypedIdAttribute : Attribute
{
}

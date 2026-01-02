using NetCommerce.SharedKernel.Application;

namespace NetCommerce.Catalog.Application.TestCommands;

public record TestCatalogCommand : ICommand<Guid>;

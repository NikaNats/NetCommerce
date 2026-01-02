using NetCommerce.SharedKernel.Application;

namespace NetCommerce.Ordering.Application.TestCommands;

public record TestOrderingCommand : ICommand<Guid>;
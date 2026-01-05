#nullable enable

namespace NetCommerce.SharedKernel.Application.Notifications;

/// <summary>
///     Template rendering abstraction for email/notification content.
///     Implementations can use Razor, Scriban, or simple string interpolation.
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    ///     Renders a template with the provided model.
    /// </summary>
    /// <param name="templateName">Name of the template (e.g., "OrderConfirmation")</param>
    /// <param name="model">Model object containing data for the template</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rendered HTML content</returns>
    Task<string> RenderAsync(string templateName, object model, CancellationToken cancellationToken = default);
}

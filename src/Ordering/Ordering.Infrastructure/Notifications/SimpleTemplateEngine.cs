#region

using System.Globalization;
using System.Reflection;
using System.Text;
using NetCommerce.SharedKernel.Application.Notifications;

#endregion

namespace NetCommerce.Ordering.Infrastructure.Notifications;

/// <summary>
///     Simple template engine using string interpolation.
///     Production systems should use RazorTemplateEngine or ScribanTemplateEngine for complex templates.
/// </summary>
public class SimpleTemplateEngine : ITemplateEngine
{
    public Task<string> RenderAsync(string templateName, object model, CancellationToken cancellationToken = default)
    {
        string html = templateName switch
        {
            "OrderConfirmation" => RenderOrderConfirmation(model),
            _ => throw new ArgumentException($"Unknown template: {templateName}", nameof(templateName))
        };

        return Task.FromResult(html);
    }

    private static string RenderOrderConfirmation(object model)
    {
        PropertyInfo[] props = model.GetType().GetProperties();
        string customerName = props.First(p => p.Name == "CustomerName").GetValue(model)?.ToString() ?? "Customer";
        string orderNumber = props.First(p => p.Name == "OrderNumber").GetValue(model)?.ToString() ?? "N/A";
        string orderId = props.First(p => p.Name == "OrderId").GetValue(model)?.ToString() ?? Guid.Empty.ToString();
        object? totalAmount = props.First(p => p.Name == "TotalAmount").GetValue(model);
        string currency = props.First(p => p.Name == "Currency").GetValue(model)?.ToString() ?? "GEL";

        // Format amount using invariant culture for consistency
        string formattedAmount = totalAmount != null
            ? ((decimal)totalAmount).ToString("N2", CultureInfo.InvariantCulture)
            : "0.00";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head><title>Order Confirmation</title></head>");
        sb.AppendLine("<body style='font-family: Arial, sans-serif; padding: 20px;'>");
        sb.AppendLine($"<h1>Thank you for your order, {customerName}!</h1>");
        sb.AppendLine($"<p>Your order <strong>{orderNumber}</strong> has been confirmed.</p>");
        sb.AppendLine("<div style='background-color: #f0f0f0; padding: 15px; margin: 20px 0;'>");
        sb.AppendLine($"<p><strong>Order ID:</strong> {orderId}</p>");
        sb.AppendLine($"<p><strong>Order Number:</strong> {orderNumber}</p>");
        sb.AppendLine($"<p><strong>Total Amount:</strong> {formattedAmount} {currency}</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("<p>We'll send you another email when your order ships.</p>");
        sb.AppendLine("<p>If you have any questions, please contact our support team.</p>");
        sb.AppendLine(
            "<p style='color: #666; font-size: 12px; margin-top: 30px;'>This is an automated message from NetCommerce. Please do not reply.</p>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}

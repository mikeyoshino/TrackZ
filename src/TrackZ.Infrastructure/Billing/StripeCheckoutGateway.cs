using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TrackZ.Infrastructure.Billing;

public sealed class StripeBillingOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string MonthlyPriceId { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;

    public bool IsConfigured => SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
        && MonthlyPriceId.StartsWith("price_", StringComparison.Ordinal)
        && IsHttps(SuccessUrl) && IsHttps(CancelUrl);

    private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
}

public sealed record StripeCheckoutSession(string Id, Uri Url);

/// <summary>
/// Test-mode provider adapter. Not exposed as a purchase endpoint until durable
/// webhook/entitlement handling and duplicate-subscription protection are connected.
/// A return URL is never evidence of successful payment.
/// </summary>
public sealed class StripeCheckoutGateway(HttpClient http, IOptions<StripeBillingOptions> options)
{
    public async Task<StripeCheckoutSession> CreateAsync(
        Guid userId, string customerId, Guid operationId, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured) throw new InvalidOperationException("Stripe test billing is not configured.");
        if (userId == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("Account and operation identifiers are required.");
        if (!customerId.StartsWith("cus_", StringComparison.Ordinal))
            throw new ArgumentException("A stored Stripe customer is required.", nameof(customerId));

        // Read the configured price from Stripe so a Dashboard mistake cannot change
        // the promised THB 59 monthly amount unnoticed.
        using var priceRequest = Request(HttpMethod.Get,
            "prices/" + Uri.EscapeDataString(settings.MonthlyPriceId), settings.SecretKey);
        using var priceResponse = await http.SendAsync(priceRequest, cancellationToken);
        priceResponse.EnsureSuccessStatusCode();
        using var price = await JsonDocument.ParseAsync(await priceResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var p = price.RootElement;
        if (!p.GetProperty("active").GetBoolean() || p.GetProperty("livemode").GetBoolean()
            || p.GetProperty("currency").GetString() != "thb"
            || p.GetProperty("unit_amount").GetInt64() != 5900
            || p.GetProperty("recurring").GetProperty("interval").GetString() != "month"
            || p.GetProperty("recurring").GetProperty("interval_count").GetInt32() != 1)
            throw new InvalidOperationException("Stripe price must be a test THB 59 monthly price.");

        using var request = Request(HttpMethod.Post, "checkout/sessions", settings.SecretKey);
        request.Headers.Add("Idempotency-Key", $"trackz-membership-{userId:D}-{operationId:D}");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["customer"] = customerId,
            ["client_reference_id"] = userId.ToString("D"),
            ["subscription_data[metadata][trackz_user_id]"] = userId.ToString("D"),
            ["line_items[0][price]"] = settings.MonthlyPriceId,
            ["line_items[0][quantity]"] = "1",
            ["success_url"] = settings.SuccessUrl,
            ["cancel_url"] = settings.CancelUrl,
            ["locale"] = "th"
        });
        // The caller offers this paid purchase after the account trial has ended.
        // Do not add another provider trial or charge during the account trial.
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var session = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = session.RootElement;
        var id = root.GetProperty("id").GetString();
        var url = root.GetProperty("url").GetString();
        if (string.IsNullOrEmpty(id) || !Uri.TryCreate(url, UriKind.Absolute, out var checkoutUri)
            || checkoutUri.Scheme != Uri.UriSchemeHttps || checkoutUri.Host != "checkout.stripe.com")
            throw new InvalidOperationException("Stripe returned an invalid Checkout session.");
        return new(id, checkoutUri);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string secret)
    {
        var request = new HttpRequestMessage(method, "https://api.stripe.com/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return request;
    }
}

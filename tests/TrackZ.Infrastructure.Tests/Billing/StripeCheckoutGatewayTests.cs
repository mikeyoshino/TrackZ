using System.Net;
using Microsoft.Extensions.Options;
using TrackZ.Infrastructure.Billing;

namespace TrackZ.Infrastructure.Tests.Billing;

public sealed class StripeCheckoutGatewayTests
{
    [Fact]
    public async Task Wrong_price_is_rejected_before_creating_a_subscription_session()
    {
        var handler = new StripeStub(6000);
        var gateway = new StripeCheckoutGateway(new HttpClient(handler), Options.Create(Settings()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(Guid.NewGuid(), "cus_test", Guid.NewGuid()));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Checkout_uses_server_price_and_account_binding_without_restarting_trial()
    {
        var userId = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var handler = new StripeStub(5900);
        var gateway = new StripeCheckoutGateway(new HttpClient(handler), Options.Create(Settings()));
        var result = await gateway.CreateAsync(userId, "cus_test", operation);
        Assert.Equal("checkout.stripe.com", result.Url.Host);
        Assert.Equal($"trackz-membership-{userId:D}-{operation:D}", handler.Idempotency);
        var body = Uri.UnescapeDataString(handler.Body!);
        Assert.Contains("mode=subscription", body);
        Assert.Contains("customer=cus_test", body);
        Assert.Contains($"subscription_data[metadata][trackz_user_id]={userId:D}", body);
        Assert.DoesNotContain("trial", body);
    }

    [Fact]
    public async Task Incomplete_or_live_configuration_never_contacts_Stripe()
    {
        var handler = new StripeStub(5900);
        var settings = Settings();
        settings.SecretKey = "sk_live_not_allowed";
        var gateway = new StripeCheckoutGateway(new HttpClient(handler), Options.Create(settings));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CreateAsync(Guid.NewGuid(), "cus_test", Guid.NewGuid()));
        Assert.Equal(0, handler.Calls);
    }

    private static StripeBillingOptions Settings() => new()
    {
        SecretKey = "sk_test_fake_for_unit_test",
        MonthlyPriceId = "price_test",
        SuccessUrl = "https://example.com/success",
        CancelUrl = "https://example.com/cancel"
    };

    private sealed class StripeStub(int amount) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public string? Idempotency { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                {
                    active = true, livemode = false, currency = "thb", unit_amount = amount,
                    recurring = new { interval = "month", interval_count = 1 }
                })) };
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Idempotency = request.Headers.GetValues("Idempotency-Key").Single();
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"cs_test_1","url":"https://checkout.stripe.com/c/pay/cs_test_1"}""") };
        }
    }
}

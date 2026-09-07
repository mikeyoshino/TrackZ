# Membership setup and current implementation boundary

The welcome screen uses the approved benefit copy and opens existing registration/login. The API exposes authenticated `GET /api/v1/membership/trial`, computed from the account's persisted creation timestamp plus one calendar month. This endpoint describes the account trial only; it is not a paid entitlement endpoint. Current access is not yet paywalled.

Registration alone does not authorize any charge. The implemented account trial has `autoRenews: false`. After expiry, the intended next step is explicit purchase confirmation. If the product instead requires a provider-backed auto-renewing trial from day one, move trial activation to provider confirmation and configure the same single trial policy across channels before shipping.

## Stripe preparation

`StripeCheckoutGateway` is a registered, test-only HTTP adapter, not a publicly available payment endpoint. It checks the configured Stripe price is active, test mode, THB 5900 minor units, recurring every month. It sends a stored customer ID and account metadata, and uses an operation-specific idempotency key. It does not grant access from the Checkout return URL.

Set these API configuration environment variables locally, never in the mobile app:

```text
Billing__Stripe__SecretKey
Billing__Stripe__MonthlyPriceId
Billing__Stripe__SuccessUrl
Billing__Stripe__CancelUrl
```

Use `sk_test_…` and a `price_…` for THB 59 monthly. The return URLs must be owned HTTPS web pages. The adapter intentionally rejects live keys while the remaining lifecycle is unconnected.

Before exposing Checkout: persist one customer mapping per TrackZ user, prevent duplicate subscriptions across providers, persist/verify/deduplicate Stripe webhook events, reconcile current subscription state, support customer portal cancellation, and test renewal failure/refund. Do not add a fresh Stripe free trial after the account has already used its month.

## Apple iOS

Digital app membership should use App Store In-App Purchase. Apple Pay through Stripe is a different product and is not the default payment route for this membership inside the iOS app.

Create an auto-renewable subscription for `com.trackz.app` in App Store Connect and provide its Product ID. Select the intended Thai monthly price there; display the localized price returned by StoreKit. Configure sandbox testers and App Store server API credentials outside source control. Implementation still needs StoreKit purchase/restore, appAccountToken binding, server transaction verification and App Store Server Notifications before purchases can be enabled.

The approved mock used an auto-renewing provider trial. The current account trial instead starts on registration and has no payment method attached. Resolve the activation flow before configuring an Apple introductory offer to avoid two months of free access or an unexpected immediate charge.

## References

- https://developer.apple.com/app-store/review/guidelines/#in-app-purchase
- https://developer.apple.com/in-app-purchase/
- https://docs.stripe.com/api/checkout/sessions/create
- https://docs.stripe.com/billing/subscriptions/webhooks

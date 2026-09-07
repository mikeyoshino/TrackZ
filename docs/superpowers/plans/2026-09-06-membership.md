# TrackZ membership implementation plan

**Goal:** Approved benefit-led welcome screen, one calendar month trial, then THB 59 monthly membership using App Store In-App Purchase on iOS and Stripe on the web.

**Architecture:** A signed-in TrackZ account owns access. The API computes trial dates from its immutable registration timestamp. Paid access must come from verified provider transactions, never a mobile flag or Checkout redirect. Apple and Stripe purchase subscriptions are separate providers of the same entitlement.

**Spec:** Approved single-screen mock `exec-67a9f554-379e-424b-bf0c-a375845626c5.png` and user instruction to support Stripe and Apple payment.

## Constraints

- Plain Thai copy, English equivalent, existing logo and Noto Sans Thai fonts.
- Introductory screen has no price. Purchase confirmation must show the actual price, renewal and cancellation terms before consent.
- One calendar month, not a fixed 30-day interval. Reinstall/login cannot restart a trial.
- No automatic charge without an approved provider subscription.
- No secrets in mobile binaries or source control. No changes to another project's Docker/API.
- Do not enforce a paid gate until purchase, restoration, cancellation and verified entitlement updates work.

## Work

- [x] Add `WelcomePage` as the signed-out root, routing trial CTA to existing registration and login to existing sign-in.
- [x] Add immutable trial-date policy with month-end and exact-expiry tests.
- [x] Expose authorized `/api/v1/membership/trial` from persisted user creation time; do not accept a client-supplied user or start date.
- [x] Prepare test-only Stripe Checkout adapter with price validation and unit tests; do not expose a purchase endpoint yet.
- [ ] Configure Stripe test monthly THB 5900 price and Checkout, signed webhooks, customer portal, durable event deduplication and provider subscription ownership.
- [ ] Configure Apple subscription product, StoreKit purchase/restore, server transaction verification, App Store notifications and account-token ownership.
- [ ] Connect membership status UI and API enforcement after sandbox lifecycle tests pass for both providers.

## External configuration required

Stripe test secret, webhook signing secret, monthly Price ID and hosted return URLs; Apple Product ID, App Store Connect server API credentials and sandbox tester. Apple prices must be read from the Store product. No live payment resources should be created while testing.

# NetCommerce Web

Next.js 16 storefront and BFF layer for NetCommerce. React Server Components,
server-side token custody, zero tokens in the browser.

## The auth boundary (read this first)

**The API is already a BFF.** `src/Api/Endpoints/Auth/AuthEndpoints.cs:7`
declares itself "BFF (Backend for Frontend) authentication endpoints", and
`KeycloakTokenProxy` performs the real token work against Keycloak.

So this app does **not** implement a parallel token authority:

- The browser holds one thing: an opaque session id in an httpOnly,
  SameSite=Lax cookie. No JWT, no refresh token, no role list is readable
  from JavaScript.
- The access/refresh token pair lives in the server process
  (`src/lib/auth/token-store.ts`).
- **Every** rotation is a call to the backend's `POST /api/v1/auth/refresh`.
  Nothing here mints, extends, or locally refreshes a token.

That last point is not stylistic. Keycloak runs native rotation
(`revokeRefreshToken=true`), where replaying a consumed refresh token revokes
the entire Keycloak session
(`src/Api/Endpoints/Auth/AuthEndpoints.cs:47-50`). A second independent
refresher would therefore present as sessions dying at random under load.

Two consequences that are easy to get wrong, both covered by
`tests/token-refresh-race.test.ts`:

- **A refresh token may never be presented twice — ever, not just concurrently.**
  `getSession()` runs on every `apiFetch`, so an RSC page firing two requests in
  one tick is enough. Concurrent callers share one in-flight promise, and
  already-presented tokens are tracked so a later call cannot replay one either.
- **Only a 400/401 from `/auth/refresh` means the token is dead.** A 429 from the
  `AuthStrict` limiter, a 502, or a dropped connection leaves the token
  unpresented and still valid; destroying the session on those signs every user
  out during a brief outage.

Sign-out calls `/auth/logout`, not `/auth/revoke`. Revoke kills only the refresh
token and leaves the server-side Keycloak session alive, so the user stays
silently signed in at the identity provider.

### Why the session registry is in-memory (on globalThis)

`src/lib/auth/server-session.ts` keeps sessions in a `Map` deliberately hung
off `globalThis`, not as a bare module-level variable: route handlers in this
Next version receive separate module instances (proven live — a session
registered from `/callback` was invisible to `/basket` in the same dev
process), while `globalThis` is shared by every route. That is correct for a
single process in local development and **incorrect** for multiple
instances — a second replica would not see the session. The swap point is the
`registry` in that file; replacing the backing store must not change any of the
semantics on `TokenStore`. Do not "fix" it by moving the refresh token into the
browser.

Sessions are bounded by `SESSION_ABSOLUTE_TTL_MS` (8h) regardless of activity,
and expired entries are swept on registry access so the Map cannot grow without
bound. A refresh can keep a session *usable* but never *immortal* — otherwise a
stolen cookie would be renewable forever. This is covered by
`tests/server-session.test.ts`.

## Keycloak client: already configured

The PKCE flow uses `clientId: netcommerce-web`, which **is** defined in the
realm the AppHost imports (`src/NetCommerce.AppHost/realms/netcommerce-realm.json:184`):

- `publicClient: true`, `standardFlowEnabled: true`, `implicitFlowEnabled: false`
- `pkce.code.challenge.method: S256` — matches `createPkcePair()` in
  `src/lib/auth/pkce.ts`
- `revoke.refresh.token: true` and `refresh.token.max.reuse: 0` — this is the
  native rotation that makes single-refresh-path handling mandatory
- `redirectUris` includes `http://localhost:3000/*`

Three cautions (the third is a fixed-and-verified past caution, kept so nobody
regresses it):

1. There is a **second, different realm file** at `tools/keycloak/netcommerce-realm.json`
   that defines only `netcommerce-api` and has no `netcommerce-web` client. The
   AppHost imports `./realms/netcommerce-realm.json` (relative to the AppHost
   project), so the richer file is the live one. The duplicate is a trap for
   anyone reading the wrong path.
2. The realm uses `clientAuthenticatorType: "client-secret"` on a
   `publicClient`. That combination is contradictory — a public client must not
   authenticate with a secret. Verified live 2026-10-03 against a running
   Keycloak: the full `/login` → form → `/callback` → BFF exchange chain works
   for `netcommerce-web` with no secret attached (`KeycloakTokenProxy` only
   sends `client_secret` for the confidential API client), and the issued
   tokens carry `realm_access.roles`. The contradictory attribute is inert, not
   fatal. Leave it alone rather than "cleaning" the realm.

3. The realm previously lacked the standard `roles`/`profile`/`email`
   client scopes (and a `user_id` mapper), so access tokens carried no roles
   and every `VendorOnly`/`CustomerOnly` endpoint 403'd. Fixed in the realm
   file and verified live. The API additionally accepts the `user_id` claim
   where it used to require `sub`, because this Keycloak deployment omits
   `sub` from access tokens on every flow (password and auth-code alike;
   `userinfo` still returns it).

## Real-time (order status over SignalR)

The API maps a Wolverine SignalR hub at `/api/messages`. It is **not** a
conventional hub with per-event methods:

- `WolverineHub` exposes exactly one public method, `ReceiveMessage(String json)`
  (verified by reflecting over `Wolverine.SignalR.dll` 6.41.0).
- The payload is a **CloudEvents** envelope passed as one JSON string. Top-level
  keys are **lowercase** (`specversion`, `datacontenttype`, `traceparent`), while
  fields inside `data` are camelCase.
- `type` is the kebab-cased CLR name: `OrderStatusChanged` → `order_status_changed`.
- When coalescing is on, `ReceiveCoalescedMessages` carries
  `{ wolverineBatch: true, items: [...] }` where each item is a *string* holding
  a whole CloudEvent document, so each needs its own `JSON.parse`.

So `connection.on('OrderStatusChanged', …)` never fires; the client subscribes to
`ReceiveMessage` and dispatches on `event.type`. Implemented in
`src/lib/real-time/` and pinned by `tests/real-time-messages.test.ts` against a
frame captured from the real envelope.

### Three status vocabularies — do not mix them

| Vocabulary | Where | Shape |
| --- | --- | --- |
| Persisted order state | `Order.cs:302-334` | numeric enum |
| Realtime push | `REALTIME_STATUS` | free-form string |
| Docs | `MESSAGING_PATTERNS.md:238-245` | **incomplete** |

The saga emits `StockSecured`, `ProcessingPayment`, `Success`, `Error`, and
`ManualInterventionRequired`. Only `Success` and `Error` appear in the messaging
doc. `StockSecured`/`ProcessingPayment` are **not** `OrderStatus` members —
`Order.cs` has `StockConfirmed` and `AwaitingValidation`. Never feed a realtime
string into `mapOrderStatus()`.

Unknown statuses are surfaced verbatim (`Unknown(x)`) rather than defaulted.
Defaulting would tell a customer their order failed when the saga is actually
parked awaiting `ManualInterventionRequired`.

### ✅ Real-time delivery is proven end-to-end

Both halves are registered in `src/Api/Extensions/Hosting/MessagingExtensions.cs`:

```csharp
opts.UseSignalR();
opts.Publish(x => { x.MessagesImplementing<IOrderNotification>().ToSignalR(); });
```

`IntegrationTestFixture` mirrors the pair, since it configures Wolverine
independently rather than calling `AddEnterpriseWolverine`. Without that mirror a
test host would have `IOrderNotification` unroutable while production had it
routed — tests passing against a host that drops every notification.

The fixture deliberately does **not** call `services.AddSignalR()`. Production
does (`src/Api/Program.cs:34`) because it also *maps* the hub endpoint, which
needs SignalR's services; the fixture maps no hub. Verified by negative control:
with `AddSignalR()` removed, delivery still passes 7/7 against a live client.

**Two independent layers of proof, both with negative controls:**

| Layer | What it proves | Without the fix |
|---|---|---|
| `SignalRTransportWiringTests` (3 tests) | Wolverine's own runtime routes `OrderStatusChanged` to the signalr transport | **2 of 3 fail** |
| Live `@microsoft/signalr` client | A real client on `/api/messages` receives the CloudEvents envelope | **6 of 7 checks fail** |

The frame as actually received:

```json
{
  "topic": null, "tenantid": null,
  "traceid": "14b0d869b0cbd08d75ac4e2948566b92", "tracestate": null,
  "data": { "orderId": "3a36d3c4-…", "status": "Success", "message": "Your order has been confirmed!" },
  "type": "order_status_changed", "specversion": "1.0"
}
```

Confirmed by observation, not inference:
- delivery goes to the **`ReceiveMessage`** operation — the hub's only public method
- envelope top-level keys are **lowercase** (`specversion`, `traceid`, `tenantid`)
- `type` is **`order_status_changed`**, kebab-cased from the CLR name
- `data` fields are **camelCase** — different casing from the envelope

`useOrderSaga` still reconciles against REST on every reconnect and on a timer
while a saga is live. That is deliberate: a dropped frame must never become an
invisible wrong state, and `ManualInterventionRequired` in particular must not be
missed.

## Headers sent to the API

| Header              | Why                                                                 |
| ------------------- | ------------------------------------------------------------------- |
| `X-Correlation-ID`  | Read by `CorrelationIdMiddleware.cs:12`. The only correlation header the API consumes. |
| `X-Idempotency-Key` | Read by `IdempotencyFilter.cs:14`. Sent on mutations only; reuse a key to make a retry safe. |
| `Authorization`     | Bearer token, from the server-side session only.                     |

**No `traceparent` header is sent.** A repo-wide search for `traceparent` in
`src/` returns zero hits, so a W3C header would correlate nothing while looking
like it did. If the API adopts W3C propagation, add it in
`src/lib/api/headers.ts` alongside a real consumer.

## Design system

Editorial/Swiss, chosen to match what this storefront actually is: a system
built around financial integrity — webhook-first payment, idempotency keys, a
documented grace period — rather than a conversion funnel. So it reads as a
ledger, not a bargain bin.

- **Type:** **Bodoni Moda** (didone display serif, italic wordmark accent),
  **Archivo** (body), **IBM Plex Mono** with `tabular-nums` — reserved strictly
  for real data: prices, SKUs, order ids. Prose labels and status text use the
  body face; mono is never costume for "technical".
  Bodoni replaced Fraunces after the Impeccable detector flagged Fraunces as one
  of the faces AI-generated UIs converge on. No Inter, Roboto, Arial or
  `system-ui` in the authored CSS.
- **Colour:** warm paper `#f4f1ea`, near-black ink `#14110f`, one vermilion
  `#c2401a` signal. Dark mode inverts to ink ground with a lifted vermilion.
- **Contrast is measured, not asserted.** WCAG ratios computed before the
  stylesheet was written: ink/paper 16.67, ink-soft/paper 8.53, vermilion/paper
  4.61. Vermilion is confined to **large** text (≥24px), rules and focus rings —
  never body copy on `--paper-deep` (4.06) or ink-on-vermilion (3.62), both
  below the 4.5 threshold. Body measure is capped at 68ch.
- **Motion:** 160–240ms, colour-only hover (a scaling card shifts layout), fully
  disabled under `prefers-reduced-motion`.
- **Status is never colour-alone.** Each state carries a distinct glyph shape
  (dot / ring / triangle / bar) plus a prose label, so state survives greyscale,
  colour-blindness and screen readers. `ManualInterventionRequired` reads "Our
  team is reviewing this order", deliberately not "failed" — a failure label
  invites a retry; a parked saga does not want one.
- **Icons:** inline SVG only, `aria-hidden` with the adjacent label carrying
  meaning. No emoji as UI icon.
- **Browser surfaces are themed:** `::selection`, `caret-color`, and scrollbars
  all derive from the palette. Left at browser defaults they are the clearest
  tell that a page was assembled rather than designed.

### Refused patterns, enforced in CI

These are bans rather than preferences, so `check.sh` fails if one returns:

- **No `.eyebrow` kicker class.** A label above a heading is banned; the heading
  carries its own weight. Small labels exist only as `.field-label` on a `<dt>`.
- **No >1px coloured border on a callout or card.** Depth comes from an offset
  plus soft-blur `box-shadow`.
- **No overused display face** (Fraunces/Inter/Roboto), no gradients, no purple
  palette, no emoji glyphs.

### Verifying the design

`scripts/check.sh` ends with `scripts/verify-render.sh`, which boots the dev
server, fetches the real HTML **and its stylesheet**, and asserts 27 invariants:
fonts, `lang`, skip link, main landmark, `:focus-visible`, reduced-motion, no
gradients, no purple palette, no emoji, breakpoints, `clamp()`, the refused
patterns above, themed browser surfaces, mono discipline, and that the
unreachable-API notice actually renders.

Two notes on how that check is scoped, both learned the hard way:

- It must fetch the stylesheet. In dev, CSS is a separate file, so an HTML-only
  check reports false failures for fonts and breakpoints.
- Banned-font assertions run against the **authored CSS**, not the whole HTML.
  Next's dev error overlay ships its own inline
  `fontFamily: system-ui, …, Roboto, …, Arial, sans-serif`; flagging that would
  be failing someone else's code.

## Configuration

| Variable              | Required            | Purpose                                              |
| --------------------- | ------------------- | ---------------------------------------------------- |
| `API_BASE_URL`        | outside development | Base URL of the .NET API (Aspire injects it)         |
| `KEYCLOAK_BASE_URL`   | outside development | Keycloak base URL (Aspire injects it)                |
| `KEYCLOAK_REALM`      | outside development | Realm name                                           |
| `KEYCLOAK_CLIENT_ID`  | outside development | Must be a public client with S256 PKCE               |
| `PUBLIC_ORIGIN`       | outside development | Origin used to build the OAuth `redirect_uri`        |

Outside development a missing variable throws a `ConfigError` naming it, rather
than silently falling back to localhost — a deploy with a misspelled variable
should fail at startup, not on every request.

`PUBLIC_ORIGIN` exists because the `redirect_uri` sent to `/authorize` and to the
token exchange must be **identical**. Deriving it from the `Host` header works in
local dev and is ambiguous behind any proxy or ingress, where `/login` and
`/callback` can observe different origins. Pin it.

The session cookie's `Secure` flag is derived from the request scheme, not from
`NODE_ENV`, so it is correct both behind TLS-terminating proxies in production
and over plain HTTP in local development. Production always forces `Secure`.

## Aspire wiring

Aspire 13.5.4 has **no** JavaScript application resource — `AddNpmApp`,
`AddViteApp`, and `AddNodeApp` are absent from `Aspire.Hosting.dll` (verified by
reflection over the pinned assembly). The frontend is registered with
`AddExecutable` in `src/NetCommerce.AppHost/Program.cs`.

Two Windows specifics, both verified by execution rather than assumed:

- `npm.cmd`, not `npm`. `AddExecutable` launches the executable directly with
  `UseShellExecute=false`, so `PATHEXT` is not consulted. Bare `npm` is a
  Git-Bash script and `Process.Start` throws `Win32Exception`.
- The dev script passes `--port 3000` as a **literal**. npm runs scripts through
  `cmd.exe`, which does not expand `${PORT:-3000}`; the unexpanded string reaches
  Next as an argument and it exits with
  `argument '${PORT:-3000}' is not a non-negative number`. Aspire does not inject
  `PORT`, so the port is fixed and the endpoint must advertise the same one.

## Commands

```bash
npm install
npm run dev         # needs API_BASE_URL / KEYCLOAK_* (the AppHost sets these)
npm run typecheck
npm test
npm run codegen:api # regenerates types from the API's OpenAPI document
```

`scripts/check.sh` is the single gate: install (if needed) → CVE guard →
typecheck → tests → production build.

On this host, dependency installs and that script must run inside the docker
sandbox (npm is gated, and the sandbox rootfs is read-only):

```bash
SECCOMP_PROFILE=none SANDBOX_ALLOW_DIR="$PWD" SANDBOX_RW=1 NETWORK=bridge \
  bash ~/hermes-config/scripts/run-sandbox.sh -lc "bash scripts/check.sh"
```

All four variables are required: the sandbox allowlist does not cover `~/source`,
the default seccomp profile file is absent, the rootfs is read-only, and npm
needs the bridge network. On Windows, `npm` is a shell shim; if Aspire cannot
launch it, use `npm.cmd`.

`next` is pinned to **16.3.8**. Versions below 16.0.7 are vulnerable to
[CVE-2025-66478](https://nextjs.org/blog/CVE-2025-66478) (CVSS 10.0, RCE via the
RSC protocol); `scripts/check.sh` fails the build if the installed version drops
below that floor.

## Storefront surface (shipped)

Against the API's real contracts, read from the endpoint and DTO source:

- Catalog (`/catalog`): server-rendered search + paging from the query string
  (`GET /api/v1/products`), shareable and refresh-safe.
- Product detail at `/products/{slug}` and `/products/id/{id}` — the id route
  exists because cards fall back to it when a product has no slug.
- Basket (`/basket`): server-rendered lines with per-row quantity update,
  removal, and clear (`GET/POST/PUT/DELETE /api/v1/basket`), all through Server
  Actions so the browser never holds a token. The basket total shown is always
  the server's `totalPrice`, never a client-side sum.

## Not implemented yet

Deliberately out of scope: checkout and Stripe Elements, the SignalR
order-tracking UI (`useOrderSaga` is written and unit-tested but no page mounts
it yet), order history pages, a production Dockerfile for the standalone
output, and the `openapi-typescript` schema file (`npm run codegen:api` —
which works again now that `/openapi/v1.json` returns 200).

Two prerequisites when the tracking UI gets built: `useOrderSaga` reconciles
against `GET /api/bff/orders/{id}`, which has no Next route yet, and it dials
`/api/messages` relative to the storefront origin while the hub lives on the
API origin — neither is reachable from a browser today. Both need a deliberate
origin strategy (same-origin proxy route), not a hardcoded API URL in the
browser bundle.

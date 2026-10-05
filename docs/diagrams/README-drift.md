# README vs source drift

Verified 2026-10-05 against the working tree at commit `9fbfbbf`. **Code is the
source of truth.** Every row below was checked by reading the file, not inferred.

## Confirmed drift — README is wrong

| README says | Source says | Evidence |
|---|---|---|
| "Wolverine 5.13" | `WolverineFx` **6.41.0** | `Directory.Packages.props:14` |
| "Wolverine 5.13.0" (tech stack table) | **6.41.0** | `Directory.Packages.props:15` |
| "Aspire 13.1" / "Aspire 13.1.0" | `Aspire.Hosting.AppHost` **13.5.4** | `Directory.Packages.props:32` |
| SignalR absent from the architecture diagram | `opts.UseSignalR()` present | `MessagingExtensions.cs:88` |
| — | `Publish(x => x.MessagesImplementing<IOrderNotification>().ToSignalR())` | `MessagingExtensions.cs:94` |

The README's architecture diagram shows
`API Gateway (Minimal APIs + Wolverine.Http)` and a messaging bus row that omits
SignalR entirely. Real-time order notification is implemented and wired; the
documentation predates it.

## Confirmed accurate

| README claim | Verified |
|---|---|
| ".NET 10" | `TargetFramework` = `net10.0` — `Directory.Build.props:28` |
| "10-state saga" | 10 values in `OrderFulfillmentState` — `OrderFulfillmentSaga.State.cs:169-181` |
| "PostgreSQL 17" | `postgres:17-alpine` in CI services — `native-aot-verification.yml:161` |
| "Redis 8" | `redis:8-alpine` — `native-aot-verification.yml:176` |
| "Native AOT, source-generated JSON" | `ApiJsonContext` with explicit registrations | 

## Documentation gaps found while generating the saga diagram

- **The brief assumed three timeout messages; there are four.** The fourth,
  `CompensationStalledTimeoutMessage` (4 hours), is the escalation path into
  `ManualInterventionRequired` and is the reason that state is non-terminal. No
  document mentions it.
- **`GracePeriodTimeout` is not a state.** It is the *trigger* for the
  `InGracePeriod → LockingInventory` transition. Reading the enum alone suggests
  it is a phase.
- **Strict timeout guards are undocumented.** `Timeouts.cs:20-24` states that a
  late or duplicate timeout must be ignored outside its origin state, "otherwise
  the customer could be charged twice or charged after cancelling". This is a
  correctness invariant with financial consequences and appears in no document.

## Suggested edits

1. `README.md:3` and the tech-stack table — Wolverine `6.41.0`, Aspire `13.5.4`.
2. `README.md` architecture diagram — add the SignalR transport row.
3. Document the four timeout messages and their distinct delays.
4. Document the strict state-guard rule for late/duplicate timeouts.
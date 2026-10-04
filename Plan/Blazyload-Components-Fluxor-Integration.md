# Blazyload.Components + Fluxor Integration

## Status

Design proposal / investigation notes.

This document describes how these libraries could work together:

```text
RonSijm.Blazyload
RonSijm.Blazyload.Components
RonSijm.Blazyload.Fluxor
RonSijm.FluxorExtensions
RonSijm.Syringe.Fluxor
```

It also evaluates whether an additional package such as:

```text
RonSijm.Blazyload.Components.Fluxor
```

would be useful.

This is a design proposal, not a claim that every integration point is already complete or production-ready. Before implementing changes, inspect the latest source of all involved libraries and verify the assumptions in this document.

---

# 1. Background

`RonSijm.Blazyload.Components` adds a component-plugin model on top of Blazyload.

A consumer can render a component owned by another feature/domain without referencing that feature's UI implementation:

```razor
<BlazyComponent Name="wonderwharf.events" />
```

The Bob's Burgers / Wonder Wharf example already demonstrates dynamic UI composition:

```text
Bob's Burgers
    ↓
requests "wonderwharf.events"
    ↓
Blazyload.Components
    ↓
Blazyload loads Wonder Wharf
    ↓
Wonder Wharf bootstrap registers services
    ↓
EventCalendar is rendered
```

A real independently developed feature may contain more than UI:

- state;
- actions;
- reducers;
- effects;
- middleware;
- services;
- persistence;
- assets;
- UI.

Fluxor makes it possible to model this state/behavior layer.

The interesting question is:

> Can a lazy-loaded Blazyload feature bring its own UI, services, Fluxor state, reducers and effects into an already-running Blazor WebAssembly application?

The answer appears to be yes, and several required pieces already exist.

---

# 2. Existing Relevant Pieces

## 2.1 RonSijm.Blazyload

Blazyload is the generic loading layer.

Its responsibilities include:

```text
assembly loading
dependency loading
feature bootstrap
dynamic service registration
service-provider rebuild/finalization
load lifecycle extensions
```

Conceptually:

```text
Blazyload
    = load and initialize a feature assembly
```

It should remain independent of component composition and Fluxor-specific concepts.

## 2.2 RonSijm.Blazyload.Components

Components adds an opinionated UI-plugin layer:

```text
stable logical component names
component catalog / manifest
runtime component registry
DynamicComponent rendering
optional generated contracts
parameter forwarding
callback forwarding
```

Conceptually:

```text
Blazyload.Components
    = find and render a UI entry point from a loaded feature
```

It should remain usable without Fluxor.

## 2.3 RonSijm.Blazyload.Fluxor

The Blazyload repository already contains:

```text
RonSijm.Blazyload.Fluxor
```

with concepts such as:

```text
LoadAssembly
LoadAssemblyEffect
LoadAssemblyForPath
LoadAssemblyForPathEffect
AssemblyLoaded
AssembliesLoadedState
DispatchAssemblyLoadedExtension
```

and Fluxor-specific Blazyload configuration through:

```csharp
providerOptions.UseFluxor(...);
```

This is already the natural home for generic:

```text
Blazyload ↔ Fluxor
```

integration.

It should not depend on `Blazyload.Components`.

## 2.4 RonSijm.Syringe.Fluxor

This is arguably the most important existing piece for dynamic Fluxor loading.

The current `WireFluxorAfterBuildExtension` inspects newly registered services and adds Fluxor types to the existing store.

Conceptually:

```csharp
if (service is IFeature feature)
{
    store.AddFeature(feature);
}

if (service is IEffect effect)
{
    store.AddEffect(effect);
}

if (service is IMiddleware middleware)
{
    store.AddMiddleware(middleware);
}
```

That is exactly the mechanism needed when a Blazyload feature is introduced after the application has already started.

---

# 3. Fluxor Supports Runtime Additions

Fluxor's `IStore` exposes runtime APIs such as:

```csharp
void AddFeature(IFeature feature);
void AddEffect(IEffect effect);
void AddMiddleware(IMiddleware middleware);
```

Therefore the existing running store can be extended when a lazy feature arrives.

Conceptually:

```text
Application starts
    ↓
Fluxor Store
    contains Bob's Burgers state

later...

Wonder Wharf is loaded
    ↓
new IFeature / IEffect / IMiddleware services appear
    ↓
existing Store.AddFeature(...)
existing Store.AddEffect(...)
existing Store.AddMiddleware(...)
    ↓
Wonder Wharf becomes part of the running Fluxor application
```

This fits Blazyload's dynamic service-provider behavior well.

---

# 4. The Combined Feature Model

A lazy-loaded feature could contain:

```text
WonderWharf
│
├── UI
│   ├── EventCalendar
│   └── RideSchedule
│
├── State
│   └── WonderWharfState
│
├── Actions
│   ├── LoadEvents
│   ├── EventsLoaded
│   └── SelectEvent
│
├── Reducers
│
├── Effects
│   └── LoadEventsEffect
│
├── Middleware
│
├── Services
│   └── WharfEventService
│
├── Contracts
│
└── Assets
    ├── CSS
    ├── images
    └── JavaScript
```

Bob's Burgers does not need a compile-time reference to the Wonder Wharf implementation. The host still publishes Wonder Wharf.

---

# 5. Desired Runtime Flow

```text
Bob's Burgers dashboard
        │
        │ requests
        ▼
<BlazyComponent Name="wonderwharf.events" />
        │
        ▼
Blazyload.Components
        │
        │ resolve catalog entry
        ▼
Blazyload
        │
        ├── load Wonder Wharf assembly
        ├── load dependencies
        ├── run bootstrap
        ├── register WharfEventService
        ├── register Fluxor Feature
        ├── register Fluxor Effects
        ├── register Middleware
        └── finalize dynamic DI
        │
        ▼
Syringe.Fluxor integration
        │
        ├── Store.AddFeature(...)
        ├── Store.AddEffect(...)
        └── Store.AddMiddleware(...)
        │
        ▼
Blazyload reports feature ready
        │
        ▼
Blazyload.Components resolves EventCalendar Type
        │
        ▼
DynamicComponent renders EventCalendar
        │
        ▼
EventCalendar dispatches LoadEvents
        │
        ▼
LoadEventsEffect
        │
        ▼
WharfEventService
        │
        ▼
EventsLoaded
        │
        ▼
Reducer updates WonderWharfState
        │
        ▼
EventCalendar rerenders
```

This demonstrates a lazy **feature slice**, not just lazy UI.

---

# 6. Architectural Boundaries

Recommended dependency structure:

```text
                         RonSijm.Blazyload
                          /             \
                         /               \
                        ▼                 ▼
      RonSijm.Blazyload.Components   RonSijm.Blazyload.Fluxor
                  │                         │
                  │                         ▼
                  │                 RonSijm.Syringe.Fluxor
                  │                         │
                  │                         ▼
                  │                       Fluxor
                  │
                  ▼
            Blazor Components
```

Valid application configurations should remain:

```text
Blazyload only

Blazyload
+ Components

Blazyload
+ Fluxor

Blazyload
+ Components
+ Fluxor
```

`Blazyload.Components` should not require Fluxor.

`Blazyload.Fluxor` should not require Components.

---

# 7. Keep Domain State Private

A consumer should not depend directly on another domain's internal Fluxor state.

Avoid this in Bob's Burgers:

```razor
@inject IState<WonderWharfState> WonderWharf
```

Instead:

```text
Wonder Wharf
    owns WonderWharfState
    owns internal actions
    owns reducers
    owns effects
    owns EventCalendar
```

Bob's Burgers interacts through public contracts.

For component-specific communication, ordinary parameters and `EventCallback<T>` remain appropriate.

---

# 8. Component Callbacks vs Fluxor Actions

Fluxor should not automatically replace `EventCallback<T>`.

Use component callbacks when the interaction is naturally parent/child UI communication:

```text
Bob renders EventCalendar
    ↓
user selects event
    ↓
EventCalendar invokes EventSelected callback
    ↓
Bob reacts
```

Use Fluxor actions when the event is conceptually application-wide:

```text
CustomerLoggedIn
ThemeChanged
ShoppingCartChanged
WharfEventSelected
FeatureActivated
```

The distinction should remain semantic rather than framework-driven.

---

# 9. Public Fluxor Contracts

There may eventually be value in exporting selected public Fluxor messages through the existing contracts mechanism.

Example:

```csharp
[BlazyContract]
public sealed record WharfEventSelected(
    Guid EventId,
    string Name);
```

This would allow both domains to share the same compiled public event type without Bob referencing the Wonder Wharf implementation assembly.

Internal feature mechanics should remain private by default.

A useful rule is:

```text
internal feature mechanics
    → stay inside the feature

cross-domain application event
    → may live in Contracts
```

---

# 10. Should `RonSijm.Blazyload.Components.Fluxor` Exist?

Possibly.

But it should exist only if there are genuinely **component-specific Fluxor integration concerns**.

The first question should be:

> If an application references both `Blazyload.Components` and `Blazyload.Fluxor`, does everything already work correctly without extra glue?

If yes, no additional runtime package is required.

That would actually be a strong architectural result.

---

# 11. What `Blazyload.Components.Fluxor` Must NOT Do

It should not duplicate responsibilities that already belong elsewhere.

Do not put this in Components.Fluxor:

```text
assembly loading
feature discovery
Store.AddFeature
Store.AddEffect
Store.AddMiddleware
generic Fluxor wiring
generic Blazyload loading actions
component manifest generation
DynamicComponent rendering
```

Those already have natural owners:

```text
Blazyload
Blazyload.Fluxor
Syringe.Fluxor
Blazyload.Components
```

A new package that merely calls:

```csharp
services.AddBlazyloadComponents();
options.UseFluxor();
```

would probably not justify another runtime abstraction.

---

# 12. What `Blazyload.Components.Fluxor` Could Own

The package becomes worthwhile if there are features specifically at the intersection of dynamic component composition and Fluxor state management.

Potential responsibilities:

## 12.1 Component Activation Actions

It could optionally dispatch lifecycle actions:

```csharp
public sealed record BlazyComponentLoaded(
    string Name,
    Type ComponentType);

public sealed record BlazyComponentActivated(
    string Name);
```

This would let global Fluxor state react to plugin/component activation.

Only add this if a real use case exists.

## 12.2 Component Loading State in Fluxor

An optional integration could project component load state into Fluxor:

```text
Unresolved
Loading
Ready
Failed
```

For example:

```csharp
BlazyComponentLoadState
{
    ["wonderwharf.events"] = Ready
}
```

Fluxor should only observe/project state owned by Blazyload. It should not become the source of truth for Blazyload's loader state machine.

## 12.3 Fluxor-Aware Public Contracts

Generated contracts might eventually expose intentionally public actions alongside component identifiers.

This is speculative and should wait until the basic component-contract API is stable.

## 12.4 Feature Readiness Before Component Rendering

There may be a valid integration concern if:

```text
assembly ready
```

does not necessarily imply:

```text
Fluxor feature/effects fully wired into the existing store
```

If that race exists, the preferred solution is to make `Blazyload.Fluxor` participate correctly in Blazyload's load-finalization lifecycle.

Then:

```csharp
await EnsureLoadedAsync(...)
```

should already mean Fluxor wiring has completed.

If core readiness guarantees this, Components does not need to know about Fluxor.

---

# 13. Recommended Initial Decision

Do not create `RonSijm.Blazyload.Components.Fluxor` immediately.

First prove the composition using:

```text
Blazyload
Blazyload.Components
Blazyload.Fluxor
Syringe.Fluxor
Fluxor
```

Build the Wonder Wharf Fluxor demo.

If all that is needed is normal setup and the dynamically rendered component can immediately resolve its Fluxor state/dispatcher dependencies, then the current package boundaries are already good.

At that point, the absence of a Components.Fluxor package is a feature rather than a missing piece.

---

# 14. Criteria for Creating `Blazyload.Components.Fluxor`

Create the package only if one or more of these turns out to be genuinely useful:

1. Dynamic component lifecycle needs to be projected into Fluxor.
2. Component resolution/load state needs an optional Fluxor representation.
3. Public generated component contracts need intentional Fluxor message integration.
4. A Fluxor-aware component wrapper removes substantial repeated application code.
5. There is a component-specific initialization/readiness handshake that cannot logically live in `Blazyload.Fluxor`.
6. Multiple real applications need the same Components + Fluxor glue.

Do not create it solely to make the package graph symmetrical.

---

# 15. Possible Package Graph If It Is Needed

```text
RonSijm.Blazyload
├── RonSijm.Blazyload.Components
├── RonSijm.Blazyload.Fluxor
└── RonSijm.Blazyload.Components.Fluxor
        ├── depends on Components
        └── depends on Blazyload.Fluxor
```

Dependency direction:

```text
Blazyload.Components.Fluxor
        ↓               ↓
   Components      Blazyload.Fluxor
        \               /
         \             /
             Blazyload
```

Core packages must never depend upward on the integration package.

---

# 16. Alternative: Meta-Package

Another possibility is:

```text
RonSijm.Blazyload.Components.Fluxor
```

as a convenience/meta-package that simply brings in:

```text
RonSijm.Blazyload.Components
RonSijm.Blazyload.Fluxor
```

This could help discoverability, but it should be documented clearly as a meta-package rather than a separate runtime architecture.

This is much less compelling than a package with actual component-specific Fluxor behavior.

---

# 17. Proposed Wonder Wharf Fluxor Example

Do not overload the current Extensive example unnecessarily.

Suggested demos:

```text
Examples/
    Simple/
    Extensive/
    Fluxor/
```

Purpose:

```text
Simple
    demonstrates basic component loading

Extensive
    demonstrates contracts/source generation/assets/services

Fluxor
    demonstrates a complete lazy-loaded feature slice
```

---

# 18. Example Fluxor Feature

Example state:

```csharp
[FeatureState]
public sealed record WonderWharfState
{
    public bool IsLoading { get; init; }

    public IReadOnlyList<WharfEvent> Events { get; init; }
        = Array.Empty<WharfEvent>();

    public Guid? SelectedEventId { get; init; }
}
```

Example internal actions:

```csharp
public sealed record LoadWharfEvents;

public sealed record WharfEventsLoaded(
    IReadOnlyList<WharfEvent> Events);

public sealed record SelectWharfEvent(
    Guid EventId);
```

Example effect:

```csharp
public sealed class LoadWharfEventsEffect
{
    private readonly WharfEventService _service;

    public LoadWharfEventsEffect(WharfEventService service)
    {
        _service = service;
    }

    [EffectMethod]
    public async Task Handle(
        LoadWharfEvents action,
        IDispatcher dispatcher)
    {
        var events = await _service.GetEventsAsync();

        dispatcher.Dispatch(
            new WharfEventsLoaded(events));
    }
}
```

Example reducer:

```csharp
[ReducerMethod]
public static WonderWharfState Reduce(
    WonderWharfState state,
    WharfEventsLoaded action)
{
    return state with
    {
        Events = action.Events,
        IsLoading = false
    };
}
```

Exact Fluxor syntax should be verified against the current Fluxor/Syringe conventions before implementation.

---

# 19. EventCalendar

Conceptually:

```razor
@inject IState<WonderWharfState> State
@inject IDispatcher Dispatcher
```

On initialization:

```csharp
Dispatcher.Dispatch(new LoadWharfEvents());
```

The important behavior is:

```text
before Wonder Wharf is loaded
    WonderWharfState does not exist in the running store

after Wonder Wharf is loaded
    WonderWharfState exists
    effects exist
    component renders against that state
```

This is the central proof of the architecture.

---

# 20. Bob's Burgers Should Stay Decoupled

Good:

```razor
<BlazyComponent
    Name="@WonderWharfComponents.Events"
    Parameters="@_parameters" />
```

Good:

```text
Bob references WonderWharf.Contracts
```

Avoid direct use from Bob of:

```text
WonderWharfState
LoadWharfEventsEffect
WharfEventService
```

Those belong to the Wonder Wharf implementation.

---

# 21. Persistence Extensions

`RonSijm.FluxorExtensions` also contains:

```text
RonSijm.Fluxor.LocalStorage
RonSijm.Fluxor.SessionStorage
```

These could eventually make a lazy-loaded feature retain state.

However, these packages should be reviewed before becoming part of the public integration demo.

The current code appears to handle `StateChanged` approximately as:

```csharp
private void OnStateChanged(object sender, EventArgs e)
{
    _localStorageService?.SetItem(Name, e);
}
```

This should be verified because it appears to persist the `EventArgs` rather than the current state value.

Do not build the main demo on these persistence packages until they are tested and modernized.

---

# 22. Runtime Effect Removal

`RonSijm.FluxorExtensions` also contains runtime effect mutation:

```text
AddEffect
RemoveEffect
StoreAccessor
```

Adding effects fits the lazy-feature model well.

Removing effects is more problematic because the existing implementation accesses Fluxor internals/private collections.

For the initial integration:

```text
support loading features
do not attempt unloading features
```

That also matches Blazyload's current model.

---

# 23. Feature Lifetime

Disposing a dynamically rendered component does not unload its feature.

Example:

```text
Bob opens Wonder Wharf calendar
    ↓
Wonder Wharf loads
    ↓
Fluxor feature is added to Store
    ↓
Bob navigates away
    ↓
EventCalendar component is disposed
```

But:

```text
Wonder Wharf assembly remains loaded
WonderWharfState remains in Fluxor Store
effects remain registered
services remain registered
```

The feature lifetime is application lifetime, not component lifetime.

Document this explicitly.

---

# 24. Readiness Contract

This integration depends strongly on Blazyload having a truthful feature-ready boundary.

Ideally:

```csharp
await assemblyCoordinator.EnsureLoadedAsync(
    "RonSijm.Demo.WonderWharf");
```

means:

```text
assembly loaded
dependencies loaded
bootstrap succeeded
dynamic services registered
service provider finalized
Blazyload extensions completed
Fluxor feature/effects wired
```

Only then should `Blazyload.Components` return the component type for rendering.

If Fluxor wiring currently happens after Blazyload reports success, fix that ordering in `Blazyload.Fluxor` / the load lifecycle rather than working around it in Components.

---

# 25. Recommended Integration Test

Create a browser/integration test proving:

1. Application starts.
2. Wonder Wharf implementation assembly is not loaded.
3. Fluxor store does not contain `WonderWharfState`.
4. User requests `wonderwharf.events`.
5. Blazyload loads Wonder Wharf.
6. Wonder Wharf bootstrap services become resolvable.
7. Wonder Wharf Fluxor feature is added to the existing store.
8. Wonder Wharf effect is registered.
9. `EventCalendar` renders.
10. Component dispatches `LoadWharfEvents`.
11. Effect executes.
12. Service returns events.
13. Reducer updates state.
14. UI rerenders.
15. Navigate away.
16. Component is disposed.
17. Wonder Wharf state/effects remain registered.
18. Navigate back.
19. Assembly is not downloaded again.
20. Feature state remains available according to the chosen lifetime.

This one scenario proves most of the architecture.

---

# 26. Additional Tests

Also verify:

```text
two components from the same lazy feature requested concurrently
feature loaded through navigation and component resolution concurrently
Fluxor effect throws during handling
duplicate Fluxor feature name
feature bootstrap fails before Fluxor wiring
Fluxor registration itself fails
service-provider rebuild occurs
feature uses middleware
feature uses generated Contracts
Release publish with trimming
.NET 10 WebAssembly publication
```

Especially verify that dynamic reducers are correctly attached to dynamically introduced features.

---

# 27. Reducer Registration Needs Review

The existing dynamic integration clearly handles:

```text
IFeature
IEffect
IMiddleware
```

There is also reducer-related code in Syringe's Fluxor integration.

Before presenting the architecture as complete, verify the exact dynamic path for:

```text
Reducer<TState, TAction>
[ReducerMethod]
generated reducer wrappers
```

Acceptance condition:

> A newly loaded Fluxor feature must receive all reducers associated with its state before the component can dispatch actions against it.

Do not assume that registering an `IFeature` alone automatically wires reducers introduced in the same lazy assembly.

---

# 28. Version Alignment

Review target frameworks and dependency versions before integrating more tightly.

At the time of this investigation:

```text
Blazyload.Components
    targets .NET 8 / 9 / 10

FluxorExtensions
    currently targets .NET 8 / 9

FluxorExtensions
    references Fluxor >= 6.6.0
```

The Fluxor-related packages should probably gain a .NET 10 target before being used in the main Components demo.

Also verify compatibility against the current supported Fluxor version rather than relying on old implementation details.

---

# 29. Naming

If the component-specific adapter becomes real, possible names are:

```text
RonSijm.Blazyload.Components.Fluxor
RonSijm.Blazyload.Fluxor.Components
```

Prefer:

```text
RonSijm.Blazyload.Components.Fluxor
```

if it primarily extends the Components developer experience.

---

# 30. Recommended Responsibility Map

```text
RonSijm.Blazyload
    load assemblies
    load dependencies
    bootstrap services
    own readiness semantics
    dynamic DI lifecycle

RonSijm.Blazyload.Components
    component manifest
    component registry
    component contracts
    DynamicComponent wrapper
    component parameters/callbacks

RonSijm.Blazyload.Fluxor
    integrate loaded assemblies with Fluxor
    Fluxor-oriented Blazyload actions/effects
    ensure dynamically registered Fluxor services enter Store

RonSijm.Syringe.Fluxor
    dynamic DI / Fluxor wiring mechanics
    register features/effects/middleware/reducers

RonSijm.Blazyload.Components.Fluxor
    OPTIONAL
    only component-specific Fluxor glue
    no generic assembly/store wiring
```

---

# 31. Recommended Implementation Sequence

## Phase 1 - Verify Existing Composition

Without creating a new package:

```text
Blazyload
+ Components
+ Blazyload.Fluxor
+ Syringe.Fluxor
```

Create a lazy Wonder Wharf feature containing real Fluxor state.

## Phase 2 - Harden Dynamic Fluxor Wiring

Test:

```text
features
effects
reducers
middleware
service-provider rebuild behavior
load ordering
failure propagation
```

Fix generic issues in `Blazyload.Fluxor` or `Syringe.Fluxor`, not Components.

## Phase 3 - Add Fluxor Demo

Create:

```text
Examples/Fluxor
```

Make the UI visibly show:

```text
assembly not loaded / loaded
Fluxor feature absent / registered
effect invoked
state updated
component rerendered
```

## Phase 4 - Evaluate Repeated Glue

Track code that exists only because Components and Fluxor are used together.

If repeated component-specific glue emerges, extract:

```text
RonSijm.Blazyload.Components.Fluxor
```

If not, do not create the package.

## Phase 5 - Optional Public Fluxor Contracts

Only after the base model is stable, investigate generated/exported public action contracts.

---

# 32. Core Architectural Story

The ecosystem can tell a coherent story:

```text
Blazyload
    loads the feature.

Blazyload.Components
    gives the feature a UI boundary.

Blazyload.Fluxor
    gives the feature a state/behavior integration path.

Contracts
    define the intentional public boundary between domains.
```

Combined:

```text
                         HOST
                           │
                     Fluxor Store
                           │
                           ▼
                       Blazyload
                   generic loading core
                    /             \
                   /               \
                  ▼                 ▼
     Blazyload.Components      Blazyload.Fluxor
        UI composition         state integration
                  \                 /
                   \               /
                    ▼             ▼
                      Wonder Wharf
                 ─────────────────────
                 EventCalendar
                 WonderWharfState
                 actions
                 reducers
                 effects
                 services
                 middleware
                 assets
```

This becomes more than dynamic components.

It becomes an architecture for independently developed, lazy-loaded Blazor feature slices.

---

# 33. Recommendation

Proceed with the Fluxor integration experiment.

Do **not** make Fluxor a dependency of `RonSijm.Blazyload.Components`.

Do **not** create `RonSijm.Blazyload.Components.Fluxor` solely for symmetry.

First prove that:

```text
Blazyload.Components
+
Blazyload.Fluxor
```

compose naturally through the existing Blazyload lifecycle.

If they do, that is the preferred architecture.

Create `RonSijm.Blazyload.Components.Fluxor` only when a real component-specific Fluxor responsibility appears.

The most likely valid reasons are:

```text
component lifecycle actions
Fluxor projection of component load state
generated public Fluxor component contracts
component-specific state/readiness helpers
```

Generic Fluxor feature/effect/reducer registration should remain in `Blazyload.Fluxor` / `Syringe.Fluxor`.

The best next implementation target is:

> A Wonder Wharf component that is not loaded at startup, brings its own Fluxor state/effects/reducers/services when first requested, and renders successfully inside Bob's Burgers without Bob depending on Wonder Wharf's implementation.

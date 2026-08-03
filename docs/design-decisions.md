# Design decisions

These are durable product and architecture decisions for the current pre-release design. Revisit them deliberately when implementation evidence or a concrete consumer requires a change.

## Alias cardinality

Each alias binds to exactly one sensor ID, and each sensor ID can have at most one configured alias.

The initial one-to-one rule provides simpler reverse lookup, unambiguous sensor decoration, a simpler configuration interface, and easier diagnostics. A display name may be changed without changing the stable alias key. Multiple compatibility aliases can be reconsidered later if a concrete consumer requires them; they are not supported speculatively.

## Alias ownership

Collectors provide stable sensor IDs and source metadata but do not assign user aliases. The alias registry owns alias validation, persistence, and decoration so collectors remain independent from configuration policy.

## Configuration format

Configuration remains versioned JSON until its operational limits justify a database. Mutable configuration is written by the service through the local API. Direct edits require a restart.

Alias records persist the stable unit code and quantity needed to represent missing hardware. Display symbols are derived from `UnitCatalog` instead of persisted, avoiding redundant metadata that can drift from the canonical catalog.

Successful replacement retains the prior active file as `config.json.previous`. Recovery is deliberate: malformed active configuration fails startup and identifies the previous file rather than loading it silently.

Physical aliases and calculated sensors share one configuration document and one key namespace. A central configuration registry serializes updates so writing either collection preserves the other. Keys use dots or underscores as separators; hyphens are excluded because `-` is formula subtraction syntax.

## Formula implementation

The first formula slice uses a tokenizer, recursive-descent parser, typed AST, and explicit dimensional inference. It does not use string substitution or a general-purpose scripting engine. Calculated definitions may depend on other calculated sensors, but dependency cycles are rejected.

No implicit unit conversion occurs. Dependency rebinding that changes a configured result type produces `CalculationError` until the definition is updated, preventing numeric values from being emitted with stale unit metadata.

## HTTP API stability

The localhost HTTP API is pre-release and may change until the first external consumer, the InfoPanel plugin, is implemented. `/api/v1` is intentionally deferred while the contract is still being shaped.

Once InfoPanel consumes the API, breaking changes must be deliberate and exposed through a versioned contract.

## Live update transport

Live updates use Server-Sent Events at `/api/sensors/stream`. Telemetry flow is one-way, so WebSockets would add protocol and session complexity without a current requirement.

Each event contains a versioned complete sensor snapshot. Event IDs are monotonic within one process but are not durable. Reconnect sends a fresh snapshot immediately; history and `Last-Event-ID` replay are deferred. The sampling interval is service-wide and bounded from 100 milliseconds to 60 seconds.

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

## HTTP API stability

The localhost HTTP API is pre-release and may change until the first external consumer, the InfoPanel plugin, is implemented. `/api/v1` is intentionally deferred while the contract is still being shaped.

Once InfoPanel consumes the API, breaking changes must be deliberate and exposed through a versioned contract.

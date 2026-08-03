# Calculated sensors

Calculated sensors turn stable aliases into new telemetry readings. They are persisted in the same configuration document as physical sensor aliases and appear through the normal sensor endpoints.

## Create a calculation

First bind aliases to the physical sensors, then create the formula:

```http
PUT /api/calculations/cooling.air.delta
Content-Type: application/json

{
  "displayName": "Radiator Air Delta",
  "formula": "cooling.air.exhaust - cooling.air.intake"
}
```

The service parses and dimension-checks the expression before saving it. The returned definition contains the inferred `quantity` and `unit`. The calculated reading is then available from:

```text
GET /api/sensors/by-alias/cooling.air.delta
GET /api/sensors/calculated:cooling.air.delta
```

Definitions can reference earlier calculated sensors. Dependency cycles are rejected. Deleting an alias or an upstream calculation does not silently delete dependent definitions; those readings report `MissingDependency` until the dependency is restored or the formula is changed.

## Formula slice

Milestone 4 supports aliases, invariant decimal constants, parentheses, unary negative, and `+`, `-`, `*`, `/`. It does not perform unit conversion. Physical addition and subtraction require exact units, while constants are dimensionless scalars used primarily for scaling. Division by zero reports `CalculationError`.

Temperature subtraction is unit-aware. For example, `40 °C - 20 °C` produces `20 Δ°C`, not an absolute `20 °C` reading. See [formula-semantics.md](formula-semantics.md) for the complete dimensional and status rules.

## API

```text
GET    /api/calculations
GET    /api/calculations/{key}
PUT    /api/calculations/{key}
DELETE /api/calculations/{key}
```

Keys use lowercase alphanumeric segments separated by dots or underscores. Hyphens are not allowed because `-` is the subtraction operator.

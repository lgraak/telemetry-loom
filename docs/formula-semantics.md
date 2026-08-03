# Formula semantics

This document defines the implemented first Milestone 4 formula slice and remains the contract for its behavior.

## Purpose and references

A calculated sensor evaluates an expression over sensor aliases. Formulas reference aliases because aliases are the user-facing stable contract; raw collector sensor IDs are intentionally excluded.

Example expression:

```text
cooling.air.exhaust - cooling.air.intake
```

Assignment is configuration structure, not expression syntax. A formula therefore contains no `result = expression` form.

## First-slice grammar

Supported tokens:

- alias operands
- decimal constants
- `+`, `-`, `*`, and `/`
- parentheses
- unary negative

Alias tokens follow the alias-key rules: lowercase alphanumeric segments separated by dots or underscores. Hyphens are excluded because they conflict with the subtraction operator. Decimal constants are invariant-culture, dimensionless scalars. Scientific notation and unit suffixes are deferred.

The implementation should use a tokenizer followed by a recursive-descent parser with conventional precedence:

```text
expression     -> additive
additive       -> multiplicative (("+" | "-") multiplicative)*
multiplicative -> unary (("*" | "/") unary)*
unary          -> "-" unary | primary
primary        -> decimal | alias | "(" expression ")"
```

A chain of regular-expression replacements is explicitly rejected. It would make precedence, unary operators, source locations, and actionable syntax errors unnecessarily fragile.

## Unit and dimensional rules

There is no implicit unit conversion. Operations that combine physical values require compatible canonical units as described below. Celsius and Fahrenheit are not interchangeable merely because both represent temperature.

### Addition and subtraction

For non-temperature physical quantities, addition and subtraction require identical unit codes and preserve that quantity and unit.

Temperature behavior is affine rather than ordinary scalar arithmetic:

| Operation | Result |
|---|---|
| absolute temperature minus absolute temperature | temperature delta |
| absolute temperature plus temperature delta | absolute temperature |
| temperature delta plus absolute temperature | absolute temperature |
| absolute temperature minus temperature delta | absolute temperature |
| temperature delta plus temperature delta | temperature delta |
| temperature delta minus temperature delta | temperature delta |
| absolute temperature plus absolute temperature | invalid |
| temperature delta minus absolute temperature | invalid |

The absolute and delta unit families must match. Celsius operations produce Celsius or delta Celsius; Fahrenheit operations produce Fahrenheit or delta Fahrenheit. Conversion between those families is deferred.

Adding or subtracting a scalar and a physical value is invalid. Scalar plus, minus, or unary negative scalar remains scalar.

### Multiplication and division

Allowed:

- scalar multiplied by a physical value preserves the physical quantity and unit
- physical value multiplied by a scalar preserves the physical quantity and unit
- physical value divided by a scalar preserves the physical quantity and unit
- scalar multiplied or divided by scalar remains scalar

Division by zero produces an explicit `CalculationError`; it never returns infinity or `NaN`.

Deferred and therefore invalid in the first slice:

- multiplication of two physical quantities, including derived-unit cases such as watts × seconds
- division of physical by physical, even when it could produce a dimensionless ratio
- division of scalar by physical
- any division that would produce a derived or reciprocal quantity

Unary negative preserves the operand's quantity and unit.

## Dependency and status propagation

All alias references are resolved before arithmetic. Missing operands never become zero, and arithmetic is performed only when every dependency is `Available` with a valid finite value.

Use this deterministic precedence when dependencies have different states:

1. `CalculationError`: an operand is invalid or faulted by an upstream calculation, or evaluation encounters an arithmetic or dimensional error
2. `MissingDependency`: an alias does not exist or cannot be resolved
3. `Unavailable`: an alias exists but its bound sensor has no current usable reading, including a collector fault
4. `Stale`: every dependency exists, but at least one reading is stale
5. `Available`: every dependency is available and evaluation succeeds

For `CalculationError`, `MissingDependency`, `Unavailable`, or `Stale`, the first implementation emits no newly calculated numeric value. It must not reuse a non-current input as though it were available.

## Calculated sensor output

A calculated sensor behaves like a collected sensor and includes:

- stable calculated-sensor ID
- display name and alias
- numeric value when available
- physical quantity
- structured unit code and derived display symbol
- status
- nullable last-successful timestamp
- metadata

On successful evaluation, `LastSuccessfulUpdate` should be the oldest dependency timestamp because the result is no fresher than its stalest input. Metadata should include the original formula, ordered dependency aliases, evaluation time, and enough error detail to diagnose parsing, resolution, dimensional, or arithmetic failures without parsing display text.

## Deferred features

The first slice does not include:

- functions
- automatic or implicit unit conversion
- raw collector sensor ID references
- multiplication of two physical quantities
- division producing derived quantities
- exponentiation
- conditionals
- assignment syntax
- historical or window functions
- cross-machine references

These remain separate design decisions. They should not be smuggled into the initial parser through undocumented syntax.

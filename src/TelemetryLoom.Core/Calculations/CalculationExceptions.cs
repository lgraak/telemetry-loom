namespace TelemetryLoom.Core.Calculations;

public sealed class CalculationValidationException(string message) : Exception(message);
public sealed class CalculationConflictException(string message) : Exception(message);

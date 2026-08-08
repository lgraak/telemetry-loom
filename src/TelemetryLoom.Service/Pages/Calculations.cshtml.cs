using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;

namespace TelemetryLoom.Service.Pages;

public sealed class CalculationsModel(
    CalculatedSensorRegistry calculations,
    CalculatedSensorCatalog calculatedSensors,
    SensorAliasRegistry aliases,
    TelemetryConfigurationRegistry configuration) : PageModel
{
    private const string KeyFieldName = $"{nameof(Input)}.{nameof(CalculationInput.Key)}";
    private const string DisplayNameFieldName = $"{nameof(Input)}.{nameof(CalculationInput.DisplayName)}";
    private const string FormulaFieldName = $"{nameof(Input)}.{nameof(CalculationInput.Formula)}";
    private const string RevisionFieldName = $"{nameof(Input)}.{nameof(CalculationInput.Revision)}";

    [BindProperty]
    public CalculationInput Input { get; set; } = new();

    [BindProperty]
    public bool ConfirmDelete { get; set; }

    public IReadOnlyList<CalculationListItem> CalculationList { get; private set; } = [];
    public IReadOnlyList<CalculationOperandItem> Operands { get; private set; } = [];
    public IReadOnlyList<CalculatedSensorDefinition> DirectDependents { get; private set; } = [];
    public CalculatedSensorDefinition? CurrentDefinition { get; private set; }
    public SensorReading? CurrentReading { get; private set; }
    public CalculationValidationView? Validation { get; private set; }
    public TelemetryConfigurationStatus Configuration { get; private set; } = null!;
    public bool IsEditing => CurrentDefinition is not null;

    [TempData]
    public string? SuccessMessage { get; set; }

    public IActionResult OnGet(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Input = new CalculationInput { Revision = configuration.GetStatus().Revision };
            LoadPage(null);
            return Page();
        }

        var existing = calculations.GetDefinition(key);
        if (existing is null)
        {
            return NotFound();
        }

        PopulateInput(existing, configuration.GetStatus().Revision);
        LoadPage(existing.Key);
        return Page();
    }

    public IActionResult OnPostValidate(string? key)
    {
        var existing = ResolveExisting(key);
        if (!string.IsNullOrWhiteSpace(key) && existing is null)
        {
            return NotFound();
        }

        if (!ValidateWorkflowIdentity(existing))
        {
            LoadPage(existing?.Key);
            return Page();
        }

        if (!ModelState.IsValid)
        {
            ModelState.AddModelError(string.Empty, "Correct the invalid submitted values before validating.");
            LoadPage(existing?.Key);
            return Page();
        }

        try
        {
            var result = calculations.Validate(Input.Key, Input.DisplayName, Input.Formula);
            Validation = ToValidationView(result);
        }
        catch (CalculationValidationException exception)
        {
            AddCalculationError(exception.Message);
        }
        catch (CalculationConflictException exception)
        {
            AddCalculationError(exception.Message);
        }

        LoadPage(existing?.Key, preserveValidation: true);
        return Page();
    }

    public IActionResult OnPostSave(string? key)
    {
        var existing = ResolveExisting(key);
        if (!string.IsNullOrWhiteSpace(key) && existing is null)
        {
            return NotFound();
        }

        if (!TryGetSubmittedRevision(out var revision))
        {
            LoadPage(existing?.Key);
            return Page();
        }

        if (!ValidateWorkflowIdentity(existing))
        {
            LoadPage(existing?.Key);
            return Page();
        }

        try
        {
            var saved = calculations.Upsert(
                Input.Key, Input.DisplayName, Input.Formula, revision);
            var reading = calculatedSensors.GetSensorByAlias(saved.Key);
            SuccessMessage =
                $"Saved calculated sensor '{saved.Key}'. Status: {reading?.Status.ToString() ?? "Unavailable"}. Configuration revision: {configuration.GetStatus().Revision}.";
            return RedirectToPage("/Calculations", new { key = saved.Key });
        }
        catch (CalculationValidationException exception)
        {
            AddCalculationError(exception.Message);
        }
        catch (CalculationConflictException exception)
        {
            AddCalculationError(exception.Message);
        }
        catch (ConfigurationRevisionConflictException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                $"{exception.Message} Your submitted values were preserved; reload or compare them with the current calculation before retrying.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(
                string.Empty,
                $"The calculated sensor could not be persisted: {exception.Message} The active file and in-memory configuration were unchanged.");
        }

        LoadPage(existing?.Key);
        return Page();
    }

    public IActionResult OnPostDelete(string key)
    {
        var existing = calculations.GetDefinition(key);
        if (existing is null)
        {
            return NotFound();
        }

        if (!TryGetSubmittedRevision(out var revision))
        {
            PopulateInput(existing, Input.Revision);
            LoadPage(key);
            return Page();
        }

        if (!ConfirmDelete)
        {
            ModelState.AddModelError(
                nameof(ConfirmDelete),
                "Confirm that deletion is non-cascading and dependent calculations will remain configured.");
            PopulateInput(existing, revision);
            LoadPage(key);
            return Page();
        }

        try
        {
            if (!calculations.Delete(key, revision))
            {
                return NotFound();
            }

            SuccessMessage =
                $"Deleted calculated sensor '{key}'. Dependent calculations were retained. Configuration revision: {configuration.GetStatus().Revision}.";
            return RedirectToPage("/Calculations");
        }
        catch (ConfigurationRevisionConflictException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                $"{exception.Message} The calculated sensor was not deleted; reload and review current dependencies before retrying.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(
                string.Empty,
                $"The calculated sensor could not be deleted: {exception.Message} The active file and in-memory configuration were unchanged.");
        }

        PopulateInput(existing, Input.Revision);
        LoadPage(key);
        return Page();
    }

    private CalculatedSensorDefinition? ResolveExisting(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : calculations.GetDefinition(key);

    private bool ValidateWorkflowIdentity(CalculatedSensorDefinition? existing)
    {
        if (existing is not null && !string.Equals(Input.Key, existing.Key, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                KeyFieldName,
                "Calculated sensor keys are immutable. Create a new calculation to use a different key.");
            return false;
        }

        if (existing is null && calculations.GetDefinition(Input.Key) is not null)
        {
            ModelState.AddModelError(
                KeyFieldName,
                $"A calculated sensor already uses key: {Input.Key}");
            return false;
        }

        return true;
    }

    private void LoadPage(string? key, bool preserveValidation = false)
    {
        Configuration = configuration.GetStatus();
        CurrentDefinition = ResolveExisting(key);
        CurrentReading = CurrentDefinition is null
            ? null
            : calculatedSensors.GetSensorByAlias(CurrentDefinition.Key);
        if (CurrentDefinition is not null)
        {
            DirectDependents = calculations.GetDirectDependents(CurrentDefinition.Key);
        }

        CalculationList = calculations.GetDefinitions()
            .Select(definition => new CalculationListItem(
                definition,
                calculatedSensors.GetSensorByAlias(definition.Key)!))
            .ToArray();
        Operands = BuildOperands();
        if (!preserveValidation)
        {
            Validation = null;
        }
    }

    private IReadOnlyList<CalculationOperandItem> BuildOperands()
    {
        var aliasOperands = aliases.GetAliases().Select(alias => new CalculationOperandItem(
            alias.Key,
            alias.DisplayName,
            alias.Quantity,
            alias.UnitSymbol,
            alias.Status,
            "Alias"));
        var calculationOperands = calculations.GetDefinitions().Select(definition =>
        {
            var reading = calculatedSensors.GetSensorByAlias(definition.Key)!;
            return new CalculationOperandItem(
                definition.Key,
                definition.DisplayName,
                definition.Quantity,
                reading.UnitSymbol,
                reading.Status,
                "Calculated");
        });
        return
        [
            .. aliasOperands.Concat(calculationOperands)
                .OrderBy(operand => operand.Origin, StringComparer.Ordinal)
                .ThenBy(operand => operand.Key, StringComparer.Ordinal)
        ];
    }

    private CalculationValidationView ToValidationView(
        CalculatedDefinitionValidationResult result)
    {
        var operands = BuildOperands().ToDictionary(operand => operand.Key, StringComparer.Ordinal);
        var dependencies = result.Dependencies.Select(key =>
        {
            var operand = operands[key];
            return new CalculationDependencyView(
                operand.Key,
                operand.DisplayName,
                operand.Quantity,
                operand.UnitSymbol,
                operand.Status);
        }).ToArray();
        return new CalculationValidationView(
            result.Definition.Quantity,
            result.Definition.Unit,
            UnitCatalog.Get(result.Definition.Unit).Symbol,
            dependencies);
    }

    private void PopulateInput(CalculatedSensorDefinition definition, long? revision)
    {
        Input = new CalculationInput
        {
            Key = definition.Key,
            DisplayName = definition.DisplayName,
            Formula = definition.Formula,
            Revision = revision
        };
    }

    private void AddCalculationError(string message)
    {
        var field = message.StartsWith("Calculated sensor key", StringComparison.Ordinal) ||
                    message.Contains("uses key", StringComparison.Ordinal)
            ? KeyFieldName
            : message.StartsWith("Display name", StringComparison.Ordinal)
                ? DisplayNameFieldName
                : FormulaFieldName;
        ModelState.AddModelError(field, message);
    }

    private bool TryGetSubmittedRevision(out long revision)
    {
        var revisionBindingIsValid = !ModelState.TryGetValue(RevisionFieldName, out var revisionEntry) ||
                                     revisionEntry.Errors.Count == 0;
        if (Input.Revision is { } submittedRevision && revisionBindingIsValid)
        {
            if (ModelState.IsValid)
            {
                revision = submittedRevision;
                return true;
            }

            ModelState.AddModelError(string.Empty, "Correct the invalid submitted values before retrying.");
            revision = default;
            return false;
        }

        ModelState.Remove(RevisionFieldName);
        ModelState.AddModelError(
            RevisionFieldName,
            "A valid configuration revision is required. Reload the page and review the current calculation before retrying.");
        revision = default;
        return false;
    }
}

public sealed class CalculationInput
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public long? Revision { get; set; }
}

public sealed record CalculationListItem(
    CalculatedSensorDefinition Definition,
    SensorReading Reading);

public sealed record CalculationOperandItem(
    string Key,
    string DisplayName,
    QuantityKind Quantity,
    string UnitSymbol,
    SensorStatus Status,
    string Origin);

public sealed record CalculationDependencyView(
    string Key,
    string DisplayName,
    QuantityKind Quantity,
    string UnitSymbol,
    SensorStatus Status);

public sealed record CalculationValidationView(
    QuantityKind Quantity,
    UnitCode Unit,
    string UnitSymbol,
    IReadOnlyList<CalculationDependencyView> Dependencies);

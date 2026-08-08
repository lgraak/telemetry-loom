using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Service.Pages;

public sealed class AliasesModel(
    SensorAliasRegistry aliases,
    CalculatedSensorRegistry calculations,
    ISensorCatalog physicalSensors,
    TelemetryConfigurationRegistry configuration) : PageModel
{
    private const string RevisionFieldName = $"{nameof(Input)}.{nameof(AliasInput.Revision)}";

    [BindProperty]
    public AliasInput Input { get; set; } = new();

    [BindProperty]
    public bool ConfirmDelete { get; set; }

    public IReadOnlyList<ResolvedSensorAlias> AliasList { get; private set; } = [];
    public IReadOnlyList<AliasTargetOption> TargetOptions { get; private set; } = [];
    public IReadOnlyList<CalculatedSensorDefinition> DirectDependents { get; private set; } = [];
    public ResolvedSensorAlias? CurrentAlias { get; private set; }
    public TelemetryConfigurationStatus Configuration { get; private set; } = null!;
    public bool IsEditing => CurrentAlias is not null;
    public bool RebindRequiresConfirmation { get; private set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public IActionResult OnGet(string? key, string? targetId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Input = new AliasInput
            {
                SensorId = physicalSensors.GetSensor(targetId ?? string.Empty)?.Id ?? string.Empty,
                Revision = configuration.GetStatus().Revision
            };
            LoadPage(null);
            return Page();
        }

        var existing = aliases.GetAlias(key);
        if (existing is null)
        {
            return NotFound();
        }

        Input = new AliasInput
        {
            Key = existing.Key,
            DisplayName = existing.DisplayName,
            SensorId = existing.SensorId,
            Revision = configuration.GetStatus().Revision
        };
        LoadPage(existing.Key);
        return Page();
    }

    public IActionResult OnPostSave(string? key)
    {
        var existing = string.IsNullOrWhiteSpace(key) ? null : aliases.GetAlias(key);
        if (!string.IsNullOrWhiteSpace(key) && existing is null)
        {
            return NotFound();
        }

        if (!TryGetSubmittedRevision(out var revision))
        {
            LoadPage(existing?.Key);
            return Page();
        }

        if (existing is not null && !string.Equals(Input.Key, existing.Key, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Input.Key), "Alias keys are immutable. Create a new alias to use a different key.");
            LoadPage(existing.Key);
            return Page();
        }

        var targetId = Input.SensorId?.Trim() ?? string.Empty;
        var target = physicalSensors.GetSensor(targetId);
        var retainingMissingTarget = existing is not null &&
                                     string.Equals(existing.SensorId, targetId, StringComparison.Ordinal);
        if (target is null && !retainingMissingTarget)
        {
            ModelState.AddModelError(nameof(Input.SensorId), "Select a currently visible physical target sensor.");
            LoadPage(existing?.Key);
            return Page();
        }

        if (existing is not null && target is not null &&
            !string.Equals(existing.SensorId, target.Id, StringComparison.Ordinal) &&
            (existing.Quantity != target.Quantity || existing.Unit != target.Unit))
        {
            RebindRequiresConfirmation = true;
            DirectDependents = calculations.GetDirectDependents(existing.Key);
            if (!Input.ConfirmRebindImpact)
            {
                ModelState.AddModelError(
                    nameof(Input.ConfirmRebindImpact),
                    "Confirm the quantity or unit change after reviewing its calculated-sensor impact.");
                LoadPage(existing.Key, preserveDependents: true);
                return Page();
            }
        }

        try
        {
            var saved = aliases.Upsert(Input.Key, Input.DisplayName, targetId, revision);
            var resultingRevision = configuration.GetStatus().Revision;
            SuccessMessage =
                $"Saved alias '{saved.Key}' targeting '{saved.SensorId}'. Status: {saved.Status}. Configuration revision: {resultingRevision}.";
            return RedirectToPage("/Aliases", new { key = saved.Key });
        }
        catch (AliasValidationException exception)
        {
            AddValidationError(exception.Message);
        }
        catch (AliasSensorNotFoundException exception)
        {
            ModelState.AddModelError(nameof(Input.SensorId), exception.Message);
        }
        catch (AliasConflictException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (ConfigurationRevisionConflictException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                $"{exception.Message} Your submitted values were preserved; reload or compare them with the current alias before retrying.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(
                string.Empty,
                $"The alias could not be persisted: {exception.Message} The active file and in-memory configuration were unchanged.");
        }

        LoadPage(existing?.Key);
        return Page();
    }

    public IActionResult OnPostDelete(string key)
    {
        var existing = aliases.GetAlias(key);
        if (existing is null)
        {
            return NotFound();
        }

        if (!TryGetSubmittedRevision(out var revision))
        {
            PopulateInput(existing);
            LoadPage(key);
            return Page();
        }

        if (!ConfirmDelete)
        {
            ModelState.AddModelError(
                nameof(ConfirmDelete),
                "Confirm that deletion is non-cascading and dependent calculations will remain configured.");
            PopulateInput(existing);
            LoadPage(key);
            return Page();
        }

        try
        {
            if (!aliases.Delete(key, revision))
            {
                return NotFound();
            }

            SuccessMessage =
                $"Deleted alias '{key}'. Dependent calculations were retained. Configuration revision: {configuration.GetStatus().Revision}.";
            return RedirectToPage("/Aliases");
        }
        catch (ConfigurationRevisionConflictException exception)
        {
            ModelState.AddModelError(
                string.Empty,
                $"{exception.Message} The alias was not deleted; reload and review current dependencies before retrying.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(
                string.Empty,
                $"The alias could not be deleted: {exception.Message} The active file and in-memory configuration were unchanged.");
        }

        PopulateInput(existing);
        LoadPage(key);
        return Page();
    }

    private void LoadPage(string? key, bool preserveDependents = false)
    {
        Configuration = configuration.GetStatus();
        AliasList = aliases.GetAliases();
        CurrentAlias = string.IsNullOrWhiteSpace(key) ? null : aliases.GetAlias(key);
        if (CurrentAlias is not null && !preserveDependents)
        {
            DirectDependents = calculations.GetDirectDependents(CurrentAlias.Key);
        }

        var assigned = aliases.GetDefinitions()
            .ToDictionary(alias => alias.SensorId, alias => alias.Key, StringComparer.Ordinal);
        var targets = physicalSensors.GetSensors()
            .Select(sensor => new AliasTargetOption(
                sensor.Id,
                sensor.DisplayName,
                sensor.Presentation?.DeviceDisplayName ?? sensor.Source,
                sensor.Quantity,
                sensor.UnitSymbol,
                sensor.Status,
                assigned.GetValueOrDefault(sensor.Id),
                false))
            .OrderBy(target => target.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .ToList();

        if (CurrentAlias is not null && targets.All(target =>
                !string.Equals(target.Id, CurrentAlias.SensorId, StringComparison.Ordinal)))
        {
            targets.Insert(0, new AliasTargetOption(
                CurrentAlias.SensorId,
                $"{CurrentAlias.DisplayName} target unavailable",
                CurrentAlias.Source,
                CurrentAlias.Quantity,
                CurrentAlias.UnitSymbol,
                SensorStatus.Unavailable,
                CurrentAlias.Key,
                true));
        }

        TargetOptions = targets;
    }

    private void PopulateInput(ResolvedSensorAlias alias)
    {
        Input = new AliasInput
        {
            Key = alias.Key,
            DisplayName = alias.DisplayName,
            SensorId = alias.SensorId,
            Revision = Input.Revision
        };
    }

    private void AddValidationError(string message)
    {
        var field = message.StartsWith("Alias key", StringComparison.Ordinal)
            ? nameof(Input.Key)
            : message.StartsWith("Display name", StringComparison.Ordinal)
                ? nameof(Input.DisplayName)
                : message.StartsWith("Sensor ID", StringComparison.Ordinal)
                    ? nameof(Input.SensorId)
                    : string.Empty;
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
            "A valid configuration revision is required. Reload the page and review the current alias before retrying.");
        revision = default;
        return false;
    }
}

public sealed class AliasInput
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SensorId { get; set; } = string.Empty;
    public long? Revision { get; set; }
    public bool ConfirmRebindImpact { get; set; }
}

public sealed record AliasTargetOption(
    string Id,
    string DisplayName,
    string DeviceName,
    QuantityKind Quantity,
    string UnitSymbol,
    SensorStatus Status,
    string? AssignedAlias,
    bool IsMissing);

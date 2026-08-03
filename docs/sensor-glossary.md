# Sensor glossary

Telemetry Loom keeps collector identity separate from presentation. Collectors still provide the stable ID, raw label, quantity, unit, status, and metadata. The enrichment layer adds a display name, short description, device grouping, metric category, confidence, and optional documentation key.

The `presentation` object is additive. Consumers that do not understand it can continue using the existing reading fields. `displayName` remains the effective name: a configured alias overrides the glossary name without changing the sensor ID or the underlying help and grouping metadata.

## Confidence

| Value | Meaning |
|---|---|
| `Known` | The driver and raw label have a documented interpretation. |
| `Derived` | The interpretation follows a documented driver model, but Telemetry Loom inferred the specific meaning from structured metadata such as a power-domain label. |
| `Generic` | The reading is safe to expose, but the available metadata does not justify a more specific interpretation. |
| `UserDefined` | The effective display name came from user configuration, such as an alias or calculated sensor. |

`Generic` is intentional. A plausible label is not promoted to `Known` without an authoritative source and fixture coverage.

## Seeded terms

| Driver and raw label | Display name | Notes | Confidence | Documentation key |
|---|---|---|---|---|
| `k10temp` / `Tctl` | CPU Control Temperature | AMD platform cooling control value. It is not necessarily a literal die temperature. | Known | `k10temp.tctl` |
| `k10temp` / `Tdie` | CPU Die Temperature | Die temperature exposed separately on supported CPUs. | Known | `k10temp.tdie` |
| `k10temp` / `TccdN` | CPU CCD N Temperature | Temperature for Core Complex Die N when supported by the CPU. | Known | `k10temp.tccd` |
| `amdgpu` / `edge` | GPU Edge Temperature | GPU on-die edge channel. | Known | `amdgpu.edge` |
| `amdgpu` / `junction` | GPU Junction Temperature | GPU hotspot or junction channel. | Known | `amdgpu.junction` |
| `amdgpu` / `mem` | GPU Memory Temperature | GPU memory thermal channel. | Known | `amdgpu.mem` |
| `amdgpu` / `sclk` | GPU Core Clock | Graphics or compute engine clock. API values remain in hertz. | Known | `amdgpu.sclk` |
| `amdgpu` / `mclk` | GPU Memory Clock | Memory clock. API values remain in hertz. | Known | `amdgpu.mclk` |
| `amdgpu` / `PPT`, `power1_input` | GPU Package Power | Current SoC power reading. On an APU it may include CPU power. | Known | `amdgpu.ppt` |
| `amdgpu` / `PPT`, `power1_average` | Average GPU Package Power | Average SoC power reading. On an APU it may include CPU power. | Known | `amdgpu.ppt` |
| `coretemp` / `Package id N` | CPU Package N Temperature | Intel package Digital Thermal Sensor reading. | Known | `coretemp.package` |
| `coretemp` / `Core N` | CPU Core N Temperature | Intel per-core Digital Thermal Sensor reading. | Known | `coretemp.core` |
| `intel_rapl*` power domains | CPU Package/Core/Uncore or DRAM Power | Domain selected from the structured driver label. This is an inference and remains `Derived`. | Derived | `intel_rapl.*` |
| `nvme` / `Composite` | NVMe Composite Temperature | Composite thermal value reported by the device. Its computation is device-specific and may not correspond to one physical point. | Known | `nvme.composite` |
| `nvme` / `Sensor N` | NVMe Temperature Sensor N | Vendor-defined additional sensor. The component is not guessed. | Generic | `nvme.sensor` |
| `acpitz*` temperature | Thermal Zone Temperature | Generic ACPI thermal-zone reading. | Generic | none |
| `iwlwifi*` temperature | Adapter Temperature | Temperature exposed by the Intel Wi-Fi driver. | Generic | none |

Unrecognized labels and drivers retain their raw label, normal quantity and unit, and stable device grouping. They receive `Generic` confidence and no documentation key.

## Sources

- [Linux `k10temp` driver documentation](https://docs.kernel.org/hwmon/k10temp.html)
- [Linux AMDGPU power and thermal monitoring](https://docs.kernel.org/gpu/amdgpu/thermal.html)
- [Linux `coretemp` driver documentation](https://docs.kernel.org/hwmon/coretemp.html)
- [Linux hwmon sysfs interface](https://docs.kernel.org/hwmon/sysfs-interface.html)
- [Linux power-capping and Intel RAPL model](https://docs.kernel.org/power/powercap/powercap.html)
- [NVM Express 1.4c specification](https://www.nvmexpress.org/wp-content/uploads/NVM-Express-1_4c-2021.06.28-Ratified.pdf)

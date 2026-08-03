# hwmon fixture capture

Telemetry Loom reads the Linux hwmon class at `/sys/class/hwmon`. The capture tool creates a small JSON snapshot that lets discovery and stable-ID behavior be reproduced without the original hardware.

## Capture

From the repository root on Linux:

```bash
dotnet run --project src/TelemetryLoom.Hwmon.Capture -- hwmon-fixture.json
```

An alternate hwmon root can be passed as the second argument:

```bash
dotnet run --project src/TelemetryLoom.Hwmon.Capture -- output.json /alternate/sys/class/hwmon
```

No root privileges should be needed. Standard hwmon attributes are intended to be world-readable. If a device denies access, do not broaden sysfs permissions; report the inaccessible path instead.

## Captured data

The fixture includes:

- capture time and hwmon root
- temporary class name such as `hwmon3`, for diagnostics only
- kernel driver name
- resolved hardware path or a clearly marked degraded identity
- optional device and channel labels
- input, average, label, and fault attributes for supported sensor types

The tool deliberately does not read serial-number attributes, device firmware, thresholds, fan controls, PWM controls, or unrelated sysfs files. A fixture still describes hardware topology and live readings, so inspect it before sharing.

Repository fixtures currently cover:

| Fixture | Broad platform coverage |
|---|---|
| `cachyos-amd.json` | CachyOS with AMD CPU/GPU, NVMe, ACPI, and Intel Wi-Fi hwmon devices |
| `mustafar-amd-proxmox.json` | AMD-based Proxmox host with AMD CPU/GPU, two NVMe devices, Ethernet, and SPD temperature devices |

Endor and Tython were considered for this enrichment slice but were not reachable with the available SSH credentials. They remain future fixture targets, not inferred coverage.

See [contributing sensor enrichment](contributing-sensor-enrichment.md) for the source, confidence, mapping, and test requirements applied to new fixtures.

## Stable IDs

An ID has this form:

```text
hwmon:{driver}:{device-key}:{sensor-type}:{channel}:{measurement}
```

`device-key` is derived from the resolved hardware path after removing temporary `hwmonN` components. Labels do not participate because users and kernel configuration can rename them. The raw path remains in metadata for troubleshooting.

When the kernel path cannot be resolved, the fixture marks the identity as `DegradedDriverIdentity`. Such sensors remain visible, but identical unlabeled devices may not be distinguishable until a stronger identity source is added.

## Native scaling

The initial collector follows kernel hwmon conventions:

| Attribute | Native representation | API representation |
|---|---:|---:|
| `tempN_input` | millidegree Celsius | Celsius |
| `fanN_input` | RPM | RPM |
| `inN_input` | millivolt | volt |
| `currN_input` | milliampere | ampere |
| `powerN_input`, `powerN_average` | microwatt | watt |
| `freqN_input` | hertz | hertz |
| `humidityN_input` | milli-percent | percent |

The kernel documentation notes that direct sysfs consumers do not automatically receive motherboard-specific `libsensors` corrections, labels, or channel-hiding rules. Telemetry Loom currently exposes the kernel values and labels. Integration with `libsensors` configuration is a later design decision.

References:

- [Linux kernel hwmon sysfs interface](https://docs.kernel.org/hwmon/sysfs-interface.html)
- [Linux kernel AMD GPU hwmon frequency definitions](https://docs.kernel.org/gpu/amdgpu/thermal.html)

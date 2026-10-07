# Potato Launcher v1.0.112 — correct GPU load

- The Optimizer now shows GPU load as the NVIDIA driver reports it (same number as nvidia-smi / GPU dashboards). The previous Windows counters under-reported DLSS 5 and similar work (e.g. ~70% shown while the GPU was 97% busy). Non-NVIDIA GPUs keep the previous measurement.

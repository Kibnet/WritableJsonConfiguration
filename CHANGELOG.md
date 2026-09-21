# Changelog

All notable changes to this project are documented in this file.

## 8.1.0 - 2026-09-21

### Added

- Add opt-in atomic writes on Windows through `WritableJsonConfigurationSource.UseAtomicWrites`.
- Serialize in-process writes to the same settings path and keep one readable `.bak` copy.
- Preserve restrictive file permissions before writing settings bytes.

### Changed

- Publish configuration data in memory only after the file commit succeeds.
- Avoid rewriting and rotating the backup when a save does not change the JSON document.

### Fixed

- Preserve nested object siblings and array tails in atomic mode.
- Reconcile memory with disk after an ambiguous commit failure, or block further writes until the configuration root is recreated.

### Compatibility

- Atomic writes are disabled by default, so existing consumers retain the previous behavior.
- The package still targets `.NET Standard 2.0`.
- Explicit atomic mode is supported on Windows; other platforms fail before writing.

### Known limitations

- Atomic mode does not coordinate multiple processes or make a series of `Set` calls transactional.
- It cannot guarantee survival of arbitrary hardware or storage failures.

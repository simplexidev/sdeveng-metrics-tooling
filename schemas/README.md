# Schema lifecycle

Schema artifacts use a stable family name plus a major version in the filename, and a
semantic version in each document's `schemaVersion` field. For example,
`evaluation-record-v1.schema.json` accepts `schemaVersion` `1.0.0`, `1.1.0`, and `1.2.0`.
Version 1.1 adds optional tool-invocation and context-isolation measurements; 1.2 adds
separate optional JEV-call and GPT-judge-token measurements. Evaluation plan v1 accepts
`1.0`, `1.1`, and `1.2`; version 1.2 adds bounded JEV settings and an OpenAI GPT fallback.
Runner inputs use the separately versioned `evaluation-plan-v1.schema.json` family and
currently emit plan `schemaVersion` `1.2`.

- Patch versions clarify validation without changing the accepted data shape.
- Minor versions add backward-compatible optional fields or enum values.
- Major versions may remove, rename, reinterpret, or make fields required and therefore
  use a new schema filename and C# migration.

Readers must reject unsupported major versions rather than guessing. Writers emit only
the current version. Published records are immutable: migration creates a new record,
preserves the source record, and records the source schema version in migration tooling
or release provenance. Migrations must be deterministic, idempotent, covered by fixtures,
and must never invent a measured value. A value that cannot be migrated faithfully is
represented with `kind: "unavailable"`, `value: null`, and a method explaining why.

Workflow-quality v2 replaces the historical roadmap Stage and assumed WorkUnit
identities with observed commit identities and optional real run identities. The v1 parser remains available
for historical private records. V1 records cannot be automatically converted to v2
when their Stage-scoped WorkUnit is not an actual product run; no run ID is invented.

The v1 dashboard aggregate schema is independent from the evaluation-record family. It
remains supported until a separately versioned public aggregation phase replaces it.
The detailed keyless analyzer output conforms to `static-cost-report-v1.schema.json`;
only its sanitized aggregate projection belongs in `sdeveng-metrics-data/public/`.
The `judge-calibration-v1` schema bounds paired live calibration to 50 labeled examples;
the example texts and resulting observation report remain private.

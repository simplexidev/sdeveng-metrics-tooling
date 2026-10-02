# SimplexiDev Engineering Toolkit Metrics Tooling

Evaluation, measurement, calibration, schemas, and publication tooling for
[`simplexidev/sdeveng`](https://github.com/simplexidev/sdeveng) on the
`develop/v3.0.0` product line.

This repository treats SimplexiDev Engineering Toolkit as the subject under test. It may run the toolkit and
consume its stable structured outputs, but it is not a plugin and is not a runtime
dependency. Human-facing methodology and usage documentation belongs in
[`simplexidev/sdeveng-docs`](https://github.com/simplexidev/sdeveng-docs).

## Repository layout

- `src/` — .NET 10 evaluator, statistics, sanitization, and validation code.
- `tests/` — keyless tests and synthetic fixtures.
- `scenarios/` — versioned scenario definitions without private prompts or source.
- `schemas/` — stable schemas for committed and published data.
- `sdeveng-metrics-data/public/` — explicit data input/output supplied to the publisher.
- `data/private/` — ignored local raw-run staging only; never commit its contents.
- `sdeveng-metrics-dashboard/` — explicit dashboard source supplied to the publisher.

Raw prompts, responses, transcripts, private source, and logs must remain local or in
short-lived CI artifacts. Only reviewed aggregates accepted by
[`schemas/public-metrics-v1.schema.json`](schemas/public-metrics-v1.schema.json) may be
committed or published. The data repository's `public/publication-manifest.json` (schema `1.0`) is the explicit allowlist and dashboard data contract;
the publisher rejects unlisted JSON, raw fields, logs, secret-like content, local absolute
paths, and malformed aggregates before creating the Pages artifact.

Run-level evaluator output uses the typed C# data model and
[`schemas/evaluation-record-v1.schema.json`](schemas/evaluation-record-v1.schema.json).
Every numeric metric carries a `kind` (`measured`, `derived`, `estimated`, or
`unavailable`), unit, and method. The [schema lifecycle policy](schemas/README.md)
defines compatibility and migration rules.

## Evaluation runner

The runner executes only through the OpenAI-backed Codex CLI. Executor and GPT-judge
model IDs are plan settings; non-OpenAI providers and Claude model identifiers are
rejected. Plans also require an executor version tag so CLI upgrades invalidate cached
baselines. Each arm/repetition receives an isolated copy of its fixture plus configured
skill and agent overlays. Timeouts are bounded and terminate the complete process tree.

Plans conform to
[`schemas/evaluation-plan-v1.schema.json`](schemas/evaluation-plan-v1.schema.json). The
[`scenarios/runner-smoke-v1.json`](scenarios/runner-smoke-v1.json) plan is a synthetic
example; replace its provenance placeholders before recording a real run.

```console
dotnet run --project src/SdevEng.Metrics -- validate-plan scenarios/runner-smoke-v1.json
dotnet run --project src/SdevEng.Metrics -- run scenarios/runner-smoke-v1.json
dotnet run --project src/SdevEng.Metrics -- run scenarios/runner-smoke-v1.json --reuse-baseline
dotnet run --project src/SdevEng.Metrics -- run scenarios/runner-smoke-v1.json --raw-dir /private/evaluation/path
dotnet run --project src/SdevEng.Metrics -- aggregate-baseline /private/evaluation/path ../sdeveng-metrics-data/public/pre-optimization-baseline.json <toolkit-sha>
dotnet run --project src/SdevEng.Metrics -- aggregate-v2-acceptance /private/evaluation/path scenarios/v2-acceptance-v1.json ../sdeveng-metrics-data/public/pre-optimization-baseline.json ../sdeveng/config/capabilities.json ../sdeveng-metrics-data/public/v2-acceptance.json <toolkit-sha>
```

Raw prompts, JSONL events, responses, failures, and per-trial records are written beneath
the selected private raw directory (default `data/private/runs/`, which is ignored).
Console output contains only compact pass/fail and reuse counts. Compatibility hashing
covers scenario expectations and prompts, fixture and overlay content, executor/model/
reasoning/executable settings, timeout, judge settings, and runner compatibility version.
An exact hash/version match is required for reuse; existing mismatches are stale and run
again.

The pre-optimization plans cover single and affected tests, test writing, compiler and
MSBuild failures, slow builds, coverage, diagnostics planning, repository mapping, log
analysis, focused review, Git/GitHub/CI, advanced .NET, and a general repository task.
They compare vanilla execution with a pinned current upstream .NET skill selection and
built-in/no-custom execution with the current custom reviewer. Repetitions are fixed at
one for this broad baseline; compatible raw trials can be reused. `aggregate-baseline`
reads only validated private evaluation records, rejects mixed toolkit revisions, and
emits a schema-validated reviewed aggregate without prompts, responses, logs, or paths.

`agent-capability-evaluation-v1.json` consumes the toolkit's native-agent audit and
capability-routing cases through evaluator-only role instructions. Scenario-specific arm
selection compares native/no-custom, current or historical custom, and candidate behavior
without installing candidate agents into the toolkit. Synthetic evidence is embedded in
the bounded prompt because nested Codex sandboxes may not permit a second shell sandbox.
The reviewed recommendation policy uses only the public disposition vocabulary and the
aggregate command verifies that every cited scenario/arm has a validated private record.

`v2-acceptance-v1.json` reruns the optimized toolkit arm over the compatible bounded
pre-v2 capability cases and three agent-boundary cases. `aggregate-v2-acceptance` compares
those validated private records with the reviewed vanilla/upstream baseline. It publishes
quality gates before efficiency, separates skill and agent pass rates, and makes missing
files/context, build/test classification, and JEV false-exclusion or avoided-context
evidence visible as coverage gaps rather than inferred zero-cost success.

```console
dotnet run --project src/SdevEng.Metrics -- aggregate-agent-capability \
  data/private/agent-capability-v1-evidence scenarios/agent-recommendations-v1.json \
  ../sdeveng-metrics-data/public/agent-capability-evaluation.json <toolkit-sha>
dotnet run --project src/SdevEng.Metrics -- validate-agent-candidates \
  ../sdeveng scenarios/agent-capability-evaluation-v1.json \
  scenarios/agent-recommendations-v1.json
```

Judging follows a strict hierarchy. Failed deterministic assertions stop immediately;
passing exact checks may proceed to a bounded JEV rubric. An accepted JEV score resolves
the case, while unavailable or low-confidence JEV results escalate to the configured
OpenAI GPT judge. Evaluation records keep JEV remote-call counts separate from GPT judge
token usage. Plans using `judge.path: "jev"` configure an OpenAI fallback in
`judge.provider`/`judge.model` and may override bounded JEV settings under `judge.jev`.
The credential is read only from `TYPESAFE_API_KEY` at the HTTP authorization boundary.

`judge-calibration-v1.json` supplies a small synthetic starting set. Live calibration is
optional, explicit, and capped at 50 examples; normal tests use mocked JEV and GPT
responses and require no key. The private report records per-example agreement,
confidence, mismatch, escalation, JEV calls, and GPT usage. Optional public output
contains only validated aggregate counts and rates. A confidence recommendation is
emitted only when at least three observed examples achieve 90% expected-outcome accuracy.

```console
dotnet run --project src/SdevEng.Metrics -- calibrate-judges \
  scenarios/judge-calibration-v1.json data/private/calibration/report.json --live
```

Pairwise GPT judging always evaluates both candidate orders. It reports a winner only
when normalized decisions agree; consistent ties remain ties and position-sensitive or
invalid results are explicit disagreements.

## Static cost and routing measurement

The deterministic analyzer inventories the sibling toolkit's skill frontmatter, complete
`SKILL.md` files, lazy Markdown references, maximum possible load, always-visible routing
surface, activation-visible content, sibling trigger overlap, and custom-agent
configuration/instructions. Its detailed local report records exact UTF-8 bytes and
characters. Token counts use a GPT-family `ceil(UTF-8 bytes / 4)` approximation because no
model tokenizer is available in the keyless evaluator; every such value is labeled
`estimated` with the method.

```console
dotnet run --project src/SdevEng.Metrics -- measure-static ../sdeveng
dotnet run --project src/SdevEng.Metrics -- measure-static ../sdeveng \
  --output /private/path/static-report.json \
  --public-output ../sdeveng-metrics-data/public/static-cost-routing.json
```

Treat the detailed report as local evaluator output. `--public-output` emits only
schema-valid sanitized aggregates for the dashboard. The versioned
`routing-delegation-v1.json` plan distinguishes should-activate, should-not-activate and
ambiguous routing. Expected/observed sets calculate false and missed activation or
delegation; structured observations also measure invoked tools, maximum delegation depth
and context isolation. Ambiguous cases are observed but left unscored.

## Workflow quality

Download or copy the toolkit's exact-commit `commit-ci-evidence-*` artifacts into an
ignored private directory. `aggregate-workflow-quality` reads their `result.json`
files directly. It also accepts private local validation and explicit repair records
conforming to [`workflow-quality-observations-v2.schema.json`](schemas/workflow-quality-observations-v2.schema.json).
Those records use commit identity and an optional real run identity; WorkUnit identity is left unset until the
product creates WorkUnits. Missing local records produce zero eligible local scopes, not
an inferred success rate. Missing CI timing produces no latency sample.

```console
dotnet run --project src/SdevEng.Metrics -- aggregate-workflow-quality \
  data/private/workflow-evidence data/private/workflow-quality-summary.json
```

The output is a private aggregate without commit IDs or transcripts. Review and
sanitize any public projection before adding it to `sdeveng-metrics-data`; this
command does not publish data. The historical v1 schema remains readable for
historical records, while new evidence uses v2.

## Develop

## Regression history and affected evaluation

Regression history preserves accepted baseline plus upstream and metrics revision lineage,
then applies deterministic quality gates and efficiency deltas. It labels quality, tokens,
tools, files/context, validation breadth, routing, delegation, JEV, and capability-coverage
changes. Underpowered comparisons are explicitly `insufficient-sample`; bounded JEV may
classify measured evidence but never invent numeric values.

Scenarios may declare `affectedPaths` as repository-relative exact paths or `/**` prefixes.
CI derives changed paths and validates deterministic affected selection without launching a
live matrix. An empty diff intentionally selects the complete suite; full matrices remain
manual or scheduled.

```console
dotnet run --project src/SdevEng.Metrics -- select-affected scenarios/agent-capability-evaluation-v1.json changed-paths.json
dotnet run --project src/SdevEng.Metrics -- validate-history ../sdeveng-metrics-data/public/regression-history.json
```

The repository requires the .NET SDK selected by `global.json`.

```console
dotnet restore SdevEng.Metrics.slnx
dotnet test SdevEng.Metrics.slnx
dotnet run --project src/SdevEng.Metrics -- validate-evaluation tests/SdevEng.Metrics.Tests/Fixtures/evaluation-valid-v1.json
dotnet run --project src/SdevEng.Metrics -- validate-public ../sdeveng-metrics-data/public/example-summary.json
dotnet run --project src/SdevEng.Metrics -- dashboard-check ../sdeveng-metrics-dashboard
dotnet run --project src/SdevEng.Metrics -- publish-pages ../sdeveng-metrics-dashboard ../sdeveng-metrics-data/public _site
dotnet run --project src/SdevEng.Metrics -- validate-plan scenarios/routing-delegation-v1.json
```

The dashboard repository publishes the generated, sanitized artifact at
<https://simplexidev.github.io/sdeveng-metrics-dashboard/>. Dashboard code uses only
project-relative paths.

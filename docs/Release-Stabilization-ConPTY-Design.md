# Release stabilization and focused ConPTY investigation

**Status:** design boundary approved on 2026-10-07 and recorded in
[PR #9](https://github.com/uniblab/Icod.Pty/pull/9); written specification awaiting review. Implementation has not
begun. Version selection, tagging, publication, and any production workaround remain separate decisions.

## Intent

Eight merged milestones now provide process hosting, interactive shutdown, scoped cleanup, session orchestration,
launch-time terminal configuration, qualified deployment layouts, recording/replay, and bounded scripted
interaction. The package still identifies itself as `0.1.0-alpha.1`, while its public surface and behavioral
contracts have grown substantially. The next milestone should reduce the cost of preserving an accidental
contract before adding another subsystem.

The selected work combines option 13 with a focused portion of option 9. Release stabilization reviews the
complete post-PR #8 contract and the evidence supporting it. The ConPTY portion turns the documented
fragmented-query warning into a bounded, repeatable experiment. These tracks meet at the release-readiness report:
the public package must describe both what it supports and what the Windows native stack cannot reliably provide.

Success does not mean that a package is published. Success means that a version decision can be made from a
reviewed API, exact-package qualification, current documentation, and classified native evidence rather than
assumption.

## Selected approach

Use a stabilization-first workflow with an independent ConPTY research gate.

1. Freeze and inventory the current public surface and observable contracts before editing them.
2. Audit compatibility, documentation, samples, package metadata, and release evidence.
3. Build a pure-C# ConPTY experiment that varies fragmentation while holding the child protocol constant.
4. Classify the native evidence before deciding whether production code should change.
5. Correct only demonstrated Icod.Pty defects that fit the reviewed compatibility boundary.
6. Requalify the exact package and published consumers, then issue a release-readiness report.

This ordering prevents an uncertain native observation from driving speculative product changes. It also keeps
the research tool useful if the outcome is a Windows limitation rather than a library bug.

Two alternatives are rejected. A ConPTY-first implementation would assume that the known symptom has an
Icod.Pty fix before isolating the failing layer. A broad terminal-compatibility campaign would mix ConPTY,
terminal emulation, host applications, and unrelated Windows console behavior into an unbounded milestone.

## Release-stabilization contract

### Public API and behavior

Treat the current compatibility baseline as the starting inventory, not automatic approval of every entry.
Review each public type and member for:

- naming, mutability, construction, equality, and forward-compatible enum/result design;
- ownership and disposal of processes, sessions, caller streams, recording streams, and native handles;
- cancellation before and after observable side effects;
- validation and maximum-deadline rules;
- separation of request acceptance, primary exit, descendant state, output completion, drain results, recording,
  matching, and final resource release;
- platform capability reporting and the difference between unsupported, unavailable, rejected, and failed;
- exception types, error context, and the prohibition on leaking terminal payload or secret input;
- behavior after exit, completion, timeout, cancellation, fault, and disposal; and
- consistency across `PtyProcess`, `PtySession`, recording, matching, and script-runner layers.

Prefer documentation and tests when the current behavior is coherent. An API or behavioral change requires a
specific defect, compatibility impact, and migration note. Additive convenience APIs are outside this milestone
unless they are necessary to correct an inconsistency that would otherwise become a release contract. Do not add
a generic observer, callback, metrics, or exporter surface during the audit.

### Package, documentation, and samples

Audit the package as a consumer receives it rather than relying only on repository builds. Verify package
metadata, target frameworks, AnyCPU claims, helper assets, notices, license expression, symbol/source metadata,
README inclusion, and platform/runtime prerequisites. Confirm that the documented framework-dependent,
self-contained, single-file, and trimmed layouts match executed package-consumer evidence. NativeAOT remains a
feasibility probe unless a separate decision promotes it.

Review the root README and sample guide for a coherent path from minimal process launch through session shutdown,
scope ownership, terminal configuration, recording, and automation. Examples must dispose owned objects, bound
waits, avoid implying that request acceptance proves process exit, and state where input or output may contain
secrets. Smoke commands must be discoverable and must describe what they establish and what they do not.

The readiness report will distinguish repository tests, hosted native CI, exact-package consumer tests, published
layout tests, and operator-observed laptop acceptance. Evidence from one category does not silently substitute for
another.

## Focused ConPTY investigation

### Question

Determine whether ConPTY can reliably deliver a protocol query to the hosted application when the same logical
input is written intact or split across multiple host writes. Identify whether any observed loss or mutation occurs
in Icod.Pty's managed forwarding, its ConPTY write boundary, the Windows pseudoconsole, or the child application's
console-input interpretation.

The investigation does not promise a native fix. It must preserve the existing limitation until evidence supports
a narrower statement.

### Experiment shape

Add an opt-in sample/test-child experiment implemented entirely in C#. The child recognizes a fixed byte sequence,
reports success through an unambiguous machine-readable response, and exits within a bounded deadline. The host
runs the same sequence using controlled write patterns:

- one intact write;
- every two-part split position;
- one-byte writes;
- selected multi-fragment boundary patterns;
- fragments with no deliberate delay; and
- fragments with bounded delays chosen to expose scheduling sensitivity.

Each case uses a fixed repetition count and records aggregate outcomes without recording unrelated terminal
content. The evidence record identifies OS/build, architecture, target framework, host environment when known,
write pattern, delay class, repetitions, successes, failures, and timeouts. Avoid wall-clock assertions tighter
than the existing CI environment can support.

The child protocol and managed forwarding layer require deterministic unit coverage. Actual ConPTY execution is
environmental evidence: the harness reports `Reproduced`, `NotReproduced`, `Inconclusive`, or `Unavailable` rather
than turning a native non-reproduction into proof that no limitation exists. Linux and macOS must explicitly skip
the Windows-only probe while retaining the ordinary cross-platform regression suite.

### Layer isolation

The experiment should compare the public/session path with the narrowest existing backend path that can be
exercised without adding a second product implementation. Controlled streams prove that Icod.Pty does not merge,
drop, reorder, or rewrite the caller's input bytes before the native boundary. Child-side reporting distinguishes
missing, combined, translated, and delayed observations where the Windows console API permits that distinction.

Do not introduce C/C++, Python, external executables, private Windows symbols, undocumented hooks, or a dependency
on a particular third-party terminal. CMD, SH, and Windows PowerShell 5.1-compatible repository tooling remains
required.

### Outcome rule

Classify the investigation before changing production:

1. **Icod.Pty defect.** A deterministic managed or interop boundary loses, reorders, or rewrites bytes before
   ConPTY accepts them. Correct it test-first if the fix preserves the approved public contract.
2. **ConPTY/native limitation.** Managed forwarding is exact, while the pseudoconsole or hosted console application
   loses or changes fragmented input. Retain the reproducer and document the verified scope; do not claim a fix.
3. **Inconclusive.** The symptom cannot be reproduced consistently or the failing layer cannot be isolated. Retain
   the prior warning, publish the experiment parameters and observations, and create no speculative workaround.

If a prospective correction would require a new public contract, semantic break, broad buffering policy, or
terminal emulator, stop and propose a separate design. The planning milestone does not pre-authorize such a change.

## Failure, security, and compatibility boundaries

All new operations must have finite repository-controlled limits. Test processes and PTYs must be disposed on
success, failure, timeout, and cancellation. A native hang must not stall the full matrix indefinitely. Research
results must not include environment variables, command history, arbitrary terminal output, credentials, or user
input. Fixed probe bytes and aggregate counts are sufficient.

No probe result weakens existing process/session cleanup or stream-ownership guarantees. No workaround may delay
ordinary writes, combine independent caller writes, or silently retry input unless a later reviewed design defines
those semantics. Existing callers and disabled probe paths must retain their behavior.

## Verification model

The milestone uses four evidence layers:

1. **Deterministic tests:** public API baseline, contract/validation tests, managed fragmentation fixtures, cleanup,
   timeout, and cancellation behavior on net8.0, net9.0, and net10.0.
2. **Native CI:** all six Windows/Linux/macOS x64/ARM64 jobs, with the ConPTY experiment active only where available.
3. **Package consumers:** exact NuGet artifacts exercised through ordinary, framework-dependent,
   self-contained, single-file, and trimmed layouts; NativeAOT remains informational.
4. **Manual acceptance:** documented Windows x64 laptop commands and observations, explicitly separated from CI.

A tranche is complete only when its implementation-plan entry records commands, counts, platform coverage, and
any expected skip or limitation. A green build alone is not release qualification.

## Milestone sequence

The detailed implementation roadmap will be written after this specification is reviewed. Its required sequence is:

- RS01: roadmap truth and baseline capture;
- RS02: public API and behavioral-contract audit;
- RS03: ConPTY experiment contract and deterministic fixtures;
- RS04: native ConPTY probe and evidence collection;
- RS05: evidence classification and any permitted correction;
- RS06: documentation, samples, package metadata, and release-material audit;
- RS07: compatibility and exact-package regression closure;
- RS08: complete six-platform qualification and manual acceptance instructions; and
- RS09: release-readiness report and remaining-decision handoff.

Implementation may refine task granularity but must preserve this dependency order: inventory before corrections,
probe before workaround, exact package before readiness, and readiness before version or publication decisions.

## Explicit exclusions

This milestone does not select or publish a version; tag or merge a release; promote NativeAOT; add wider Unix
support; implement live terminal-state changes; add input recording, timed replay, regex/text matching, branching
automation, screen interpretation, persistent sessions, resource controls, generic diagnostics exporters, or a
security sandbox. Findings may recommend later work without expanding this milestone.

## Acceptance

The written implementation roadmap must map RS01-RS09 to exact files, tests, commands, evidence, and stop conditions.
Implementation is acceptable when the audits are resolved or explicitly documented, the ConPTY outcome is
classified without overclaiming, supported package layouts pass the full matrix, required manual commands are
recorded, and the readiness report states whether a prerelease can be selected and what blockers remain. Merge,
version selection, tagging, and publication require separate user decisions.

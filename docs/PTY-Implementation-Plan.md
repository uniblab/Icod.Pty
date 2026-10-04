# Implementation plan

Execution is authorized by the user's implementation approval.

- [x] Define spawn, byte-stream, resize, wait, terminate and disposal contracts.
- [x] Establish failing API tests before implementation.
- [x] Implement AnyCPU shared API, Unix helper/backend and Windows ConPTY backend.
- [x] Add terminal, argument, resize, cancellation, exit, concurrency and disposal integration tests.
- [x] Package the helper and add consumer/publish smoke checks and a sample.
- [ ] Verify all three target frameworks and all six CI platforms.
- [ ] Review native resource lifetime and package contents; resolve findings.

The execution workspace reset during implementation. Sources were restored from the session; all restored code must be rebuilt and retested. The original pre-reset Linux net10 tests passed 13 cases.

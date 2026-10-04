# Implementation plan

Execution is authorized by the user's implementation approval.

- [x] Define spawn, byte-stream, resize, wait, terminate and disposal contracts.
- [x] Establish failing API tests before implementation.
- [x] Implement AnyCPU shared API, Unix helper/backend and Windows ConPTY backend.
- [x] Add terminal, argument, resize, cancellation, exit, concurrency and disposal integration tests.
- [x] Package the helper and add consumer/publish smoke checks and a sample.
- [ ] Verify all three target frameworks and all six CI platforms.
- [x] Review native resource lifetime and package contents; resolve findings.

## Verification record

- Linux local: 18 integration tests pass on each of net8.0, net9.0 and net10.0; Release build has zero warnings/errors.
- Six-platform CI exercises all frameworks, package contents, consumer launch, and published helper assets.
- Review findings resolved: redirected Windows standard handles, Darwin ARM64 variadic ABI, older glibc openpty lookup, and nonexecutable PATH shadows.
- Exact minimum-build Windows laptop acceptance remains a user check; README contains commands.

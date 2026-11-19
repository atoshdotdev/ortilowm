# AGENTS.md

## Project overview

Tessel is a native Windows 11 tiling window manager written in C# on
.NET 10.

The project is organized into three production projects and a test
project:

``` text
Tessel/
├── src/
│   ├── Tessel.App/
│   ├── Tessel.Core/
│   └── Tessel.Platform.Windows/
└── tests/
    └── Tessel.Core.Tests/
```

### Project responsibilities

-   **Tessel.Core** contains platform-independent domain models, layout
    algorithms, workspace behavior, and services. Keep it free of Win32
    and Windows-specific dependencies.
-   **Tessel.Platform.Windows** contains Windows-specific functionality,
    including Win32 interop, window enumeration, hooks, native windows,
    positioning, focus, monitor integration, and drawing.
-   **Tessel.App** is the composition and application layer. It wires
    dependencies together, handles application-level coordination, and
    translates user commands into domain operations.
-   **Tessel.Core.Tests** contains unit tests for platform-independent
    behavior. Prefer testing domain behavior here without requiring a
    running Windows desktop.

Respect the existing project references:

-   `Tessel.Platform.Windows` may reference `Tessel.Core`.
-   `Tessel.App` may reference both `Tessel.Core` and
    `Tessel.Platform.Windows`.
-   `Tessel.Core` must not reference either outer project.

## Instructions for agents

You are expected to make changes directly and carefully, not merely
describe possible changes.

Before editing:

1.  Inspect the relevant code and its callers.
2.  Read the existing tests for the behavior being changed.
3.  Trace the full execution path across Core, Platform.Windows, and App
    where applicable.
4.  Identify existing invariants and compatibility requirements.
5.  Make the smallest coherent change that solves the problem without
    introducing unnecessary abstractions.

Do not assume that a proposed implementation is correct just because it
compiles. Check its behavior against the existing architecture and
tests.

When a task is ambiguous, inspect the codebase first. Ask a concise
clarification only when an important product or behavior decision cannot
be inferred safely.

## Architecture principles

### Separation of concerns

-   Keep domain decisions and layout-tree behavior in `Tessel.Core`.
-   Keep Win32 calls, native handles, hooks, message loops, and native
    drawing in `Tessel.Platform.Windows`.
-   Keep application orchestration and dependency composition in
    `Tessel.App`.
-   Do not move Windows-specific behavior into Core to make an
    implementation more convenient.
-   Do not let Core depend on UI, operating-system, or infrastructure
    projects.

### Object-oriented design

-   Prefer clear responsibilities and cohesive types.
-   Follow the Single Responsibility Principle where it improves clarity
    and maintainability.
-   Prefer explicit dependencies through constructors over hidden global
    state or service location.
-   Encapsulate mutable collections. Expose read-only views or focused
    operations rather than allowing arbitrary external mutation.
-   Prefer composition over inheritance when either would work.
-   Avoid speculative interfaces, generic frameworks, wrapper types, and
    abstractions that have no meaningful second use or clear boundary.
-   Do not create large "manager" or "coordinator" classes that
    accumulate unrelated responsibilities. When modifying an existing
    large class, avoid expanding its responsibilities further.

### Maintainability and performance

-   Prioritize correctness, predictable behavior, and maintainability.
-   Avoid unnecessary allocations in frequently executed paths such as
    layout calculation, window-event handling, and focus navigation.
-   Do not introduce timers, polling, sleeps, or arbitrary delays to
    hide ordering or synchronization problems. Prefer event-driven or
    explicitly coordinated solutions.
-   Do not optimize based on guesses. Preserve a simple implementation
    unless profiling or a demonstrated hot path justifies added
    complexity.
-   Keep logging useful and avoid leaving temporary diagnostic output in
    production paths.

## Domain and window-manager behavior

Tessel aims to provide behavior comparable to tiling window managers
such as i3 and Sway, adapted to native Windows behavior.

### Workspaces

-   Workspaces are globally unique and retain their own independent
    layout state.
-   A workspace can be assigned to a monitor; monitor assignment and
    workspace identity are separate concepts.
-   Preserve the existing multi-monitor behavior:
    -   Different monitors may display different workspaces
        simultaneously.
    -   Switching to a workspace already visible on another monitor
        should activate that monitor rather than duplicate or swap the
        workspace.
    -   An unused workspace should replace the current monitor's
        workspace according to the existing workspace-manager rules.
-   Do not casually rewrite monitor assignment, reconnection, or
    disconnection logic. These behaviors are sensitive and must be
    tested against the existing implementation.

### Container tree

The container tree is the primary layout model for tiled windows.

-   Each workspace owns an independent container tree.
-   The tree supports nested containers with horizontal and vertical
    split orientations.
-   Horizontal splits place children side by side; vertical splits place
    children above and below one another.
-   Split ratios determine the relative allocation of available space.
-   A leaf may contain a tiled window or be empty.
-   Floating windows remain outside the tiled container tree.
-   Layout calculation should remain a pure Core operation and produce
    placement data. Applying placements to actual Windows windows
    belongs in the Windows platform layer.
-   Preserve parent-child consistency, node identity where the existing
    operations promise it, and valid focused-node/window relationships.
-   When removing a window, ensure empty branches are cleaned up without
    corrupting remaining tree structure.
-   Validate handles and target nodes before mutating the tree. Avoid
    partially completed operations on failure.

### Focus and navigation

-   Distinguish between the selected/focused window and the structurally
    focused container.
-   Focusing a parent or child container must not accidentally change
    the operating-system foreground window unless that behavior is
    explicitly intended.
-   Preserve container focus during operations that are intended to keep
    the current structural focus.
-   Directional navigation in the container-tree layout should use the
    tree's spatial structure and calculated geometry, not merely the
    order of a flat window list.
-   Legacy layout modes may continue using geometry-based navigation
    where appropriate.
-   A directional move/swap and a structural insertion/reparenting
    operation are different domain operations. Keep their APIs and
    semantics distinct.
-   Do not change focus synchronization behavior without tracing its
    interaction with `LayoutCoordinator`, `WorkspaceManager`, and native
    foreground-window changes.

### Floating and minimized windows

-   Floating windows are not members of the tiled layout tree.
-   Minimized windows must remain minimized when switching workspaces.
-   Restoring a minimized window should return it to its owning
    workspace without unexpectedly changing other workspace assignments.
-   Do not include minimized or floating windows in tiled placement
    calculations unless the existing behavior explicitly requires it.

### Monitors, DPI, and native placement

-   Monitor enumeration, work areas, monitor handles, DPI, and native
    window positioning are platform concerns.
-   Preserve per-monitor DPI correctness.
-   Do not add timer-based delayed layout workarounds for DPI or
    window-positioning issues.
-   Be careful when changing display-change handling, monitor
    reconnection, or monitor-selection modes.
-   Keep the existing native placement pipeline intact unless the task
    specifically requires changing it.

### Native overlays and headers

-   Native top bars, layout headers, popups, and
    focused-window/container borders are Windows platform features.
-   Preserve click-through and no-activate behavior where currently
    used.
-   Be mindful that Win32 `COLORREF` values use `0x00BBGGRR` ordering,
    not ordinary RGB integer ordering.
-   Do not replace working native drawing or overlay behavior with a
    different mechanism without a clear reason.

## Windows interop rules

-   Prefer source-generated interop with `[LibraryImport]` over
    `[DllImport]`.
-   Types containing `[LibraryImport]` declarations must be `partial`.
-   Imported methods must be declared `static partial`.
-   Keep native signatures, calling conventions, structure layouts,
    constants, and handle ownership accurate.
-   Use `SafeHandle` or another explicit ownership strategy when a
    native resource requires deterministic release and the existing
    design supports it.
-   Ensure callbacks passed to native APIs remain alive for as long as
    Windows may invoke them.
-   Unhook, unregister, destroy, and release native resources
    deterministically.
-   Handle native failures deliberately. Do not silently treat an
    invalid handle or failed Win32 call as success.
-   Keep unsafe code limited to places where it is required and document
    non-obvious invariants.
-   Consider trimming and Native AOT compatibility when adding interop,
    callbacks, reflection, or dynamically discovered types.

## Native AOT and .NET

-   Target .NET 10, following the target frameworks already defined by
    the project files.
-   Keep `Tessel.Core` platform-independent and compatible with its
    existing AOT settings.
-   Prefer compile-time-known types and source generation.
-   Avoid reflection-heavy designs, runtime code generation, and dynamic
    loading unless there is a compelling, documented requirement.
-   Do not add dependencies without checking their target-framework
    support, trimming behavior, Native AOT compatibility, licensing, and
    maintenance status.
-   Follow the repository's existing nullable-reference-type and
    implicit-using conventions.

## Testing expectations

Tests are part of the design contract. Update or add tests whenever
domain behavior changes.

At minimum:

1.  Add unit tests for new Core behavior.
2.  Cover success cases, invalid inputs, boundary conditions, and
    failure paths.
3.  Test nested container trees, not only flat layouts, when changing
    tree behavior.
4.  Test focus state and selected-window state independently where
    relevant.
5.  Test that failed operations do not leave partially mutated state.
6.  Preserve existing tests unless the intended behavior has
    deliberately changed and the tests are updated to express the new
    contract.

Do not delete, weaken, or broadly rewrite tests just to make a change
pass.

Useful commands from the repository root:

``` powershell
dotnet build
dotnet test
```

Run focused tests while iterating, then run the full test suite before
finishing. If a Windows-only behavior cannot be exercised in the current
environment, state that clearly and identify the manual verification
still needed.

## Change workflow

For each task:

1.  **Understand** --- inspect relevant types, call sites, tests, and
    project boundaries.
2.  **Plan** --- identify the behavior to preserve and the smallest safe
    change.
3.  **Implement** --- make a cohesive change; avoid unrelated
    refactoring.
4.  **Test** --- add/update tests and run build plus tests.
5.  **Review** --- inspect the final diff for accidental behavior
    changes, dead code, temporary logging, and unnecessary complexity.
6.  **Report** --- summarize what changed, why, test results, and any
    remaining manual verification.

When editing a method:

-   Check every call site before changing its signature or semantics.
-   Keep distinct operations distinct. For example, do not repurpose a
    structural `MoveWindow` operation to mean "swap two window handles."
-   Preserve public behavior unless the task explicitly requests a
    behavior change.
-   Prefer explicit names that communicate whether an operation focuses,
    selects, swaps, inserts, reparents, or moves a window.

## Agent safety and repository hygiene

-   Do not overwrite user changes or discard uncommitted work.
-   Do not run destructive Git commands such as `git reset --hard`,
    `git clean -fd`, or force-push unless the user explicitly requests
    them.
-   Do not modify generated build output under `bin/` or `obj/`.
-   Do not commit, push, publish, release, or change repository remotes
    unless explicitly asked.
-   Do not add secrets, credentials, personal data, or machine-specific
    paths to source control.
-   Do not silently change project-wide formatting, analyzers, nullable
    settings, target frameworks, or package versions as part of an
    unrelated task.
-   Keep changes scoped and explain any necessary deviation.

## Completion report

When finishing a task, provide:

-   A concise summary of the implementation.
-   The important files changed.
-   The build and test commands run, with their actual results.
-   Any behavior that still needs manual testing on Windows.
-   Any known limitations or follow-up work.

Never claim that a build, test, or manual verification passed unless it
was actually run and observed.

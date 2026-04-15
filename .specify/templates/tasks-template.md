---

description: "Task list template for feature implementation"
---

# Tasks: [FEATURE NAME]

**Feature Directory**: `specs/[###-feature-name]/`  
**Prerequisites**: `plan.md` (required), `spec.md` (required)  
**Constitution**: `.specify/memory/constitution.md`

## Path Conventions (Project-Anchored — NON-NEGOTIABLE)

⚠️ **All new files must follow the directory contract from the project constitution:**

- **Service interfaces**: `src/RentalTurnManager.Core/Services/IXxxService.cs`
- **Service implementations**: `src/RentalTurnManager.Core/Services/XxxService.cs`
- **Shared models**: `src/RentalTurnManager.Models/XxxModels.cs`
- **Lambda handlers**: `src/RentalTurnManager.XxxLambda/Function.cs`
- **Service tests**: `src/RentalTurnManager.Tests/Services/XxxServiceTests.cs`
- **Lambda tests**: `src/RentalTurnManager.Tests/XxxFunctionTests.cs`
- **CloudFormation**: `infrastructure/cloudformation/main.yaml`

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths matching the directory contract above

<!-- 
  ============================================================================
  IMPORTANT: The tasks below are SAMPLE TASKS for illustration purposes only.
  
  The /speckit.tasks command MUST replace these with actual tasks based on:
  - User stories from spec.md (with their priorities P1, P2, P3...)
  - Feature requirements from plan.md
  - Module placement rules from constitution.md
  
  Tasks MUST follow TDD: tests written first, confirmed failing, then implementation.
  Tasks MUST be organized by user story so each story can be independently delivered.
  DO NOT keep these sample tasks in the generated tasks.md file.
  ============================================================================
-->

---

## Phase 1: Setup & Verification

**Purpose**: Confirm constitution checks pass before writing any code

- [ ] T001 Verify `.specify/memory/constitution.md` interface-first and module placement checks
- [ ] T002 Run `dotnet restore src/RentalTurnManager.sln` — confirm clean restore
- [ ] T003 Run existing tests: `dotnet test src/RentalTurnManager.sln` — confirm all pass before changes

---

## Phase 2: Shared Models (if required)

**Purpose**: Define cross-project data contracts in `RentalTurnManager.Models` first

**⚠️ Only if new types are used across multiple projects**

- [ ] T004 [P] Create `src/RentalTurnManager.Models/[XxxModels.cs]` with new model types
- [ ] T005 Add `/// <summary>` XML doc comments to all new public types

---

## Phase 3: Service Contracts (Interface-First)

**Purpose**: Define the `IXxxService` interface before any implementation

- [ ] T006 [P] Create `src/RentalTurnManager.Core/Services/IXxxService.cs` — interface only, no implementation
- [ ] T007 Add method signatures with appropriate nullable return types and `Task<>` for async

---

## Phase 4: User Story 1 — [Title] (Priority: P1) 🎯 MVP

**Goal**: [Brief description of what this story delivers]  
**Independent Test**: `dotnet test --filter "FullyQualifiedName~XxxServiceTests"`

### 🔴 TDD Phase 1: Write Failing Tests

- [ ] T008 [P] [US1] Create `src/RentalTurnManager.Tests/Services/XxxServiceTests.cs` with xUnit test class
- [ ] T009 [P] [US1] Write `[Fact]` tests using `Mock<Idependency>` (Moq) and `.Should()` (FluentAssertions)
- [ ] T010 [US1] **Run tests, confirm they FAIL** — `dotnet test src/RentalTurnManager.sln` ← 🔴 Must fail first

### 🟢 TDD Phase 2: Minimal Implementation

- [ ] T011 [US1] Create `src/RentalTurnManager.Core/Services/XxxService.cs` implementing `IXxxService`
- [ ] T012 [US1] Register `IXxxService` / `XxxService` in Lambda DI container (`Function.ConfigureServices`)
- [ ] T013 [US1] **Run tests, confirm they PASS** — `dotnet test src/RentalTurnManager.sln` ← 🟢 Must pass

### 🔵 TDD Phase 3: Refactor

- [ ] T014 [US1] Refactor implementation; `/// <summary>` on all new public members
- [ ] T015 [US1] **Run tests, confirm still PASS** ← 🔵 Must not break

**Checkpoint**: `dotnet test src/RentalTurnManager.sln` — User Story 1 tests green

---

## Phase 5: User Story 2 — [Title] (Priority: P2)

**Goal**: [Brief description of what this story delivers]  
**Independent Test**: [xUnit filter for this story's tests]

### 🔴 TDD Phase 1: Write Failing Tests

- [ ] T016 [P] [US2] Add xUnit tests in `src/RentalTurnManager.Tests/Services/XxxServiceTests.cs`
- [ ] T017 [US2] **Run tests, confirm FAIL** ← 🔴

### 🟢 TDD Phase 2: Minimal Implementation

- [ ] T018 [US2] Implement [feature] in existing or new service
- [ ] T019 [US2] **Run tests, confirm PASS** ← 🟢

**Checkpoint**: `dotnet test src/RentalTurnManager.sln` — User Stories 1 AND 2 green

---

[Add more user story phases as needed, following the same TDD pattern]

---

## Phase N: Lambda Handler Integration (if required)

**Purpose**: Wire new services into Lambda entry point(s)

- [ ] TXXX Write Lambda handler tests in `src/RentalTurnManager.Tests/XxxFunctionTests.cs` (mock all services)
- [ ] TXXX Update `Function.cs` to use new service via DI
- [ ] TXXX **Run full test suite**: `dotnet test src/RentalTurnManager.sln --configuration Release`

---

## Phase N+1: Infrastructure (if required)

**Purpose**: Declare new AWS resources in CloudFormation

- [ ] TXXX Update `infrastructure/cloudformation/main.yaml` with new Lambda function, IAM role/policy, or Step Functions changes
- [ ] TXXX Update `infrastructure/cloudformation/parameters/dev.json` and `prod.json` if new parameters added

---

## Phase N+2: Final Verification

- [ ] TXXX Run full test suite: `dotnet test src/RentalTurnManager.sln --configuration Release --collect:"XPlat Code Coverage"`
- [ ] TXXX Run build: `dotnet build src/RentalTurnManager.sln --configuration Release` — zero warnings
- [ ] TXXX Verify no regressions against existing tests
- [ ] TXXX Confirm all new public members have `/// <summary>` XML doc comments

**Purpose**: Improvements that affect multiple user stories

- [ ] TXXX [P] Documentation updates in docs/
- [ ] TXXX Code cleanup and refactoring
- [ ] TXXX Performance optimization across all stories
- [ ] TXXX [P] Additional unit tests (if requested) in tests/unit/
- [ ] TXXX Security hardening
- [ ] TXXX Run quickstart.md validation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation
- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Models within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together (if tests requested):
Task: "Contract test for [endpoint] in tests/contract/test_[name].py"
Task: "Integration test for [user journey] in tests/integration/test_[name].py"

# Launch all models for User Story 1 together:
Task: "Create [Entity1] model in src/models/[entity1].py"
Task: "Create [Entity2] model in src/models/[entity2].py"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2
   - Developer C: User Story 3
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence

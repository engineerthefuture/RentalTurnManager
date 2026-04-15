# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: specs/[###-feature-name]/spec.md

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach]

## Technical Context (Project-Anchored — NON-NEGOTIABLE)

⚠️ **The following tech stack is locked by the project constitution and cannot be changed:**

**Language/Version**: C# / .NET 10 (`net10.0`)  
**Lambda Runtime**: AWS Lambda .NET 10 (`Amazon.Lambda.Core` 2.x)  
**Testing**: xUnit 2.6.x + Moq 4.20.x + FluentAssertions 6.12.x + coverlet  
**Build Command**: `dotnet build src/RentalTurnManager.sln --configuration Release`  
**Test Command**: `dotnet test src/RentalTurnManager.sln --configuration Release`  
**Project Type**: Multi-Lambda serverless (.NET solution)  
**IaC**: AWS CloudFormation (`infrastructure/cloudformation/main.yaml`)  
**Constraints**: No always-on servers; all state in S3 / SecretsManager; no hardcoded credentials

## Constitution Compliance Check

*GATE: Must pass before starting implementation.*

- [ ] **Interface-First**: Every new service has a paired `IXxxService` interface
- [ ] **Module Placement**: New business logic in `RentalTurnManager.Core`, new shared models in `RentalTurnManager.Models`, Lambda-specific code in its own Lambda project
- [ ] **No Circular Dependencies**: `Core` and `Models` do not reference Lambda projects
- [ ] **TDD**: Tests written and confirmed failing before implementation begins
- [ ] **AWS-Native**: All AWS interactions use official `AWSSDK.*` packages; no hardcoded secrets
- [ ] **Nullable Safety**: Build produces zero nullable warnings; `/// <summary>` on new public members
- [ ] **Directory Contract**: All new files placed at paths matching the constitution's directory contract

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature-name]/
├── spec.md              # Feature specification (/speckit.specify output)
├── plan.md              # This file (/speckit.plan output)
└── tasks.md             # Task list (/speckit.tasks output — NOT created by /speckit.plan)
```

### Source Code Changes

```text
src/
├── RentalTurnManager.Models/
│   └── [XxxModels.cs]                  # New shared model types (if needed)
├── RentalTurnManager.Core/
│   └── Services/
│       ├── [IXxxService.cs]            # New service interface
│       └── [XxxService.cs]            # New service implementation
├── RentalTurnManager.Lambda/           # Changes to main Lambda (if needed)
│   └── Function.cs
├── RentalTurnManager.[Xxx]Lambda/      # New or modified Lambda (if needed)
│   └── Function.cs
└── RentalTurnManager.Tests/
    ├── [XxxFunctionTests.cs]           # Lambda handler tests
    └── Services/
        └── [XxxServiceTests.cs]        # Service unit tests

infrastructure/
└── cloudformation/
    └── main.yaml                       # CloudFormation updates (new Lambda, IAM, etc.)
```

**Structure Decision**: [Document which projects are changed and why, referencing constitution module placement rules]

## Implementation Phases

### Phase 0: Verification and Preparation

- Confirm constitution checks above all pass
- Confirm no naming conflicts with existing services (`src/RentalTurnManager.Core/Services/`)
- Confirm NuGet package versions are compatible (`dotnet restore` clean)

### Phase 1: Models (if required)

Define any new shared model types in `RentalTurnManager.Models`.  
*Only needed if new cross-project data structures are required.*

### Phase 2: Service Interfaces + Implementations (Core)

Follow interface-first: create `IXxxService` contract, then `XxxService` implementation.  
Register new services in the Lambda DI container (`Function.ConfigureServices`).

### Phase 3: Lambda Handler Changes

Update or create Lambda `Function.cs` with injected service dependencies.

### Phase 4: Test Coverage (TDD — tests written in Phase 1-3 before implementation)

All new services and Lambda handlers covered by xUnit tests in `RentalTurnManager.Tests`.  
Use Moq for all AWS SDK and service dependencies; FluentAssertions for all assertions.

### Phase 5: Infrastructure (if required)

Update `infrastructure/cloudformation/main.yaml` for new Lambda functions, IAM policies, or Step Functions changes.

## Complexity Tracking

> **Fill ONLY if Constitution checks have violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., direct AWS SDK call in Lambda without service layer] | [specific reason] | [why service abstraction insufficient] |

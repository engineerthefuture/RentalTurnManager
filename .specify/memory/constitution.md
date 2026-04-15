# RentalTurnManager Constitution

## Core Principles

### I. Interface-First / Dependency Injection

Every service must be expressed as an interface (`IXxxService`) before implementation. Business logic lives in `RentalTurnManager.Core` and is consumed via DI throughout all Lambda entry points. Direct instantiation of service implementations inside business logic is forbidden.

**Source**: Every service in `src/RentalTurnManager.Core/Services/` has a paired interface (e.g., `IBookingParserService` / `BookingParserService`). All Lambda constructors wire services via `IServiceProvider`.  
**Verification**: No `new XxxService()` calls in Lambda handlers; all services injected via constructor or `serviceProvider.GetRequiredService<IXxx>()`.

### II. Single-Responsibility Lambdas

Each Lambda function has exactly one responsibility. The main Lambda scans emails and triggers workflows. The Calendar Lambda generates and sends ICS invites. The Callback Lambda handles cleaner HTTP responses and signals Step Functions. Cross-Lambda coordination happens exclusively through Step Functions state machine transitions — never through direct Lambda-to-Lambda calls in business logic.

**Source**: Three separate Lambda projects: `RentalTurnManager.Lambda`, `RentalTurnManager.CalendarLambda`, `RentalTurnManager.CallbackLambda`.  
**Verification**: No `AmazonLambdaClient` calls within `RentalTurnManager.Core`; orchestration is the Step Functions workflow's concern.

### III. Models as Shared Contracts (NON-NEGOTIABLE)

All data structures shared across projects must live in `RentalTurnManager.Models`. Lambda-specific request/response types (e.g., `CalendarEmailRequest`) may be defined locally, but any type crossing module boundaries belongs in Models.

**Source**: `RentalTurnManager.Models` is referenced by all other projects. `Booking`, `PropertyConfiguration`, `CleanerWorkflowInput`, etc. are defined there.  
**Verification**: No duplicate model definitions across projects; inter-project DTOs use Models namespace.

### IV. Test-First Discipline (TDD Enforced)

All new features follow TDD Red-Green-Refactor. Tests are written first, confirmed failing, then minimal implementation is added to make them pass.

**Test Framework**: xUnit + Moq + FluentAssertions + coverlet  
**Test Location**: `src/RentalTurnManager.Tests/` (unit tests) and `src/RentalTurnManager.Tests/Services/` (service-level tests)  
**Run Command**: `dotnet test src/RentalTurnManager.sln --configuration Release`  
**Coverage Requirement**: New code ≥ 80%; core booking/cleaner workflow logic 100%  
**Verification**: Moq verifies service interactions; FluentAssertions used for all assertions (no bare `Assert.*`).

### V. AWS-Native / Serverless-First

Infrastructure is AWS-first and serverless. No always-on servers. All AWS service interactions go through the official AWS SDK (`AWSSDK.*` packages). Configuration is injected via environment variables (`PROPERTIES_CONFIG`, `MESSAGE_TEMPLATES`) at Lambda cold-start — no local file config in production. Infrastructure is declared in CloudFormation (`infrastructure/cloudformation/main.yaml`).

**Source**: All Lambda projects reference `AWSSDK.*` NuGet packages; config loaded from env vars in `Function()` constructors.  
**Verification**: No hardcoded credentials or AWS resource ARNs in source code; all secrets via `SecretsManager`.

### VI. Nullable Safety and Code Quality

All projects target .NET 10 with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`. Null-return paths are expressed via nullable return types (`Booking?`, `CleanerContact?`). Every public file carries the standard block-comment header (author, date, purpose).

**Source**: All `.csproj` files have `<Nullable>enable</Nullable>`. `IBookingParserService` returns `Booking?`.  
**Verification**: Build must produce zero nullable warnings; all public members have `/// <summary>` XML doc comments.

### VII. No Duplication — Search Before Creating

Before creating any new class, interface, or utility, search the codebase for existing implementations. The Models project and Core services already provide reusable building blocks.

**Verification**: Code review checks for duplicate model or service definitions across projects.

## Tech Stack Anchoring

**All SDD specs and plans for this project must use this locked tech stack:**

| Category | Technology | Version | Non-replaceable |
|----------|-----------|---------|-----------------|
| Language | C# | .NET 10 (net10.0) | ✓ |
| Lambda Runtime | AWS Lambda .NET 10 | `Amazon.Lambda.Core` 2.x | ✓ |
| Testing | xUnit | 2.6.x | ✓ |
| Mocking | Moq | 4.20.x | ✓ |
| Assertions | FluentAssertions | 6.12.x | ✓ |
| Coverage | coverlet | 6.0.x | ✓ |
| Email (IMAP) | MailKit / MimeKit | 4.x | ✓ |
| Build | `dotnet build` / `dotnet publish` | .NET 10 SDK | ✓ |
| IaC | AWS CloudFormation | — | ✓ |
| CI/CD | GitHub Actions | — | ✓ |
| Serialization | `System.Text.Json` | built-in | ✓ |
| DI | `Microsoft.Extensions.DependencyInjection` | 8.x | ✓ |

## Module Architecture

### Project Type

**Project Structure**: Multi-project .NET solution (5 projects + 1 test project)  
**Solution File**: `src/RentalTurnManager.sln`

### Module List and Responsibilities

| Project | Path | Category | Core Responsibility | Allowed Code Types |
|---------|------|----------|--------------------|--------------------|
| `RentalTurnManager.Lambda` | `src/RentalTurnManager.Lambda/` | Entry Lambda | Email scan, booking parse, Step Functions trigger | Lambda handler, DI setup, `Function.cs` |
| `RentalTurnManager.CalendarLambda` | `src/RentalTurnManager.CalendarLambda/` | Calendar Lambda | ICS generation, SES calendar email delivery | Lambda handler, ICS builder |
| `RentalTurnManager.CallbackLambda` | `src/RentalTurnManager.CallbackLambda/` | Callback Lambda | API Gateway handler, Step Functions task signal | Lambda handler, HTTP response builder |
| `RentalTurnManager.Core` | `src/RentalTurnManager.Core/` | Business Logic | All service interfaces + implementations | `IXxxService` interfaces, `XxxService` implementations |
| `RentalTurnManager.Models` | `src/RentalTurnManager.Models/` | Shared Contracts | All cross-project data models | Model classes, enums, record types |
| `RentalTurnManager.Tests` | `src/RentalTurnManager.Tests/` | Tests | All unit and integration tests | xUnit test classes, fixtures, mocks |

### Module Dependency Graph

```
RentalTurnManager.Lambda
├── RentalTurnManager.Core
└── RentalTurnManager.Models

RentalTurnManager.CalendarLambda
└── (standalone — local types only; no Core dependency)

RentalTurnManager.CallbackLambda
└── (standalone — local types only; no Core dependency)

RentalTurnManager.Tests
├── RentalTurnManager.Lambda
├── RentalTurnManager.Core
├── RentalTurnManager.Models
├── RentalTurnManager.CallbackLambda
└── RentalTurnManager.CalendarLambda
```

**Dependency Rules**:
- `RentalTurnManager.Core` must NOT reference any Lambda project
- `RentalTurnManager.Models` must NOT reference any Lambda or Core project (pure data contracts)
- New cross-cutting types → `RentalTurnManager.Models`; new business logic → `RentalTurnManager.Core`
- No circular references

### New Feature Code Placement

| Code Type | Target Project | Target Path |
|-----------|---------------|-------------|
| New service interface | `RentalTurnManager.Core` | `src/RentalTurnManager.Core/Services/IXxxService.cs` |
| Service implementation | `RentalTurnManager.Core` | `src/RentalTurnManager.Core/Services/XxxService.cs` |
| New shared model / DTO | `RentalTurnManager.Models` | `src/RentalTurnManager.Models/XxxModels.cs` |
| New Lambda entry point | New Lambda project | `src/RentalTurnManager.XxxLambda/Function.cs` |
| Unit / integration test | `RentalTurnManager.Tests` | `src/RentalTurnManager.Tests/Services/XxxServiceTests.cs` |
| Lambda handler test | `RentalTurnManager.Tests` | `src/RentalTurnManager.Tests/XxxFunctionTests.cs` |
| CloudFormation resource | `infrastructure/` | `infrastructure/cloudformation/main.yaml` |
| Step Functions definition | `infrastructure/` | `infrastructure/stepfunctions/` |

## Directory Contract

| Code Type | Standard Location | Naming Convention |
|-----------|-------------------|-------------------|
| Service interfaces | `src/RentalTurnManager.Core/Services/` | `IXxxService.cs` (PascalCase, I-prefix) |
| Service implementations | `src/RentalTurnManager.Core/Services/` | `XxxService.cs` |
| Shared models | `src/RentalTurnManager.Models/` | `XxxModels.cs` (grouped by domain) |
| Lambda handlers | `src/RentalTurnManager.XxxLambda/` | `Function.cs` |
| Unit tests (services) | `src/RentalTurnManager.Tests/Services/` | `XxxServiceTests.cs` |
| Unit tests (Lambda) | `src/RentalTurnManager.Tests/` | `XxxFunctionTests.cs` |
| CloudFormation | `infrastructure/cloudformation/` | `main.yaml`, params in `parameters/` |
| Step Functions | `infrastructure/stepfunctions/` | `xxx-workflow.json` |
| Runtime config | `config/` | `properties.json`, `message-templates.json` |

## Governance

1. This constitution is the highest guidance for all SDD workflow artifacts
2. All specs must reflect serverless / AWS-native constraints
3. All plans must lock the tech stack defined above
4. All tasks must follow the directory contract and TDD cycle
5. Modifying the constitution requires documenting change reasons and updating dependent templates

**Version**: 1.0.0 | **Ratified**: 2026-04-14 | **Source**: Brownfield Bootstrap

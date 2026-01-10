# Contributing to RentalTurnManager

Thank you for your interest in contributing! This document provides guidelines for contributing to the project.

## Development Setup

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/rental-turn-manager.git`
3. Install .NET 10 SDK
4. Install dependencies: `dotnet restore src/RentalTurnManager.sln`
5. Create a feature branch: `git checkout -b feature/your-feature-name`

## Code Standards

### C# Style Guide

- Follow Microsoft C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and under 50 lines when possible
- Use async/await for asynchronous operations

### Testing Requirements

- Write unit tests for all new functionality
- Maintain 80%+ code coverage
- Use AAA pattern (Arrange, Act, Assert)
- Mock external dependencies
- Test both success and failure scenarios

### Pull Request Process

1. Update README.md or relevant documentation if needed
2. Add or update tests for your changes
3. Ensure all tests pass: `dotnet test`
4. Update the CHANGELOG.md with your changes
5. Create a pull request with a clear title and description
6. Link any related issues

## Commit Messages

Follow conventional commits format:

- `feat: Add new feature`
- `fix: Fix bug description`
- `docs: Update documentation`
- `test: Add or update tests`
- `refactor: Code refactoring`
- `chore: Maintenance tasks`

## Testing

```bash
# Run all tests
dotnet test src/RentalTurnManager.sln

# Run with coverage
dotnet test src/RentalTurnManager.sln --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter FullyQualifiedName~BookingParserServiceTests
```

## Adding New Booking Platforms

To add support for a new booking platform:

1. Update `BookingParserService.cs` with new parser method
2. Add platform identifier to property configuration
3. Update email scanner to recognize new platform emails
4. Add comprehensive tests
5. Update documentation

## Questions?

Feel free to open an issue for discussion before starting work on major features.

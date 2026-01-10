# RentalTurnManager

A serverless AWS application for automating rental property turnover scheduling by monitoring booking emails and coordinating with cleaning staff.

## Overview

RentalTurnManager automates the process of scheduling property cleanings when new bookings are received. It monitors an IMAP email account for booking confirmations from platforms like AirBNB, VRBO, and Booking.com, then automatically contacts preferred cleaners to schedule turnovers.

## Features

- **Email Monitoring**: Scans IMAP inbox for booking confirmations on a scheduled basis
- **Multi-Platform Support**: Handles bookings from AirBNB, VRBO, and Booking.com
- **Automated Cleaner Coordination**: Contacts cleaners in priority order until one confirms availability
- **Calendar Integration**: Sends calendar invites upon cleaner confirmation
- **Property Configuration**: Maintains property metadata, addresses, and cleaner preferences
- **Multi-Environment**: Supports dev and prod deployments
- **Secure Credentials**: Uses AWS Secrets Manager for email credentials

## Architecture

### Components

1. **EmailScanner Lambda (C#)**: Scheduled function that checks IMAP inbox for new booking emails
2. **Step Functions Workflow**: Orchestrates the cleaner coordination process
3. **CloudFormation Stack**: Defines all AWS infrastructure
4. **GitHub Actions**: Automated deployment pipeline

### AWS Services Used

- AWS Lambda (C# .NET 10)
- AWS Step Functions
- AWS Secrets Manager
- Amazon EventBridge (scheduling)
- Amazon SES (email sending)
- AWS Systems Manager Parameter Store (configuration)
- AWS CloudFormation (infrastructure as code)

## Project Structure

```
rental-turn-manager/
├── src/
│   ├── RentalTurnManager.Lambda/          # Main Lambda function
│   ├── RentalTurnManager.Core/            # Core business logic
│   ├── RentalTurnManager.Models/          # Data models
│   └── RentalTurnManager.Tests/           # Unit tests
├── infrastructure/
│   ├── cloudformation/
│   │   ├── main.yaml                      # Main CloudFormation template
│   │   └── parameters/
│   │       ├── dev.json                   # Dev environment parameters
│   │       └── prod.json                  # Prod environment parameters
│   └── stepfunctions/
│       └── cleaner-workflow.json          # Step Functions definition
├── .github/
│   └── workflows/
│       └── deploy.yml                     # CI/CD pipeline
├── config/
│   ├── properties.json                    # Property configurations
│   └── message-templates.json             # Email message templates
└── README.md
```

## Configuration

### GitHub Secrets

Set the following secrets in your GitHub repository:

- `EMAIL_USERNAME`: IMAP email account username
- `EMAIL_PASSWORD`: IMAP email account password
- `AWS_ACCESS_KEY_ID`: AWS access key for deployment
- `AWS_SECRET_ACCESS_KEY`: AWS secret access key

### GitHub Variables

Set the following variables in your GitHub repository:

- `OWNER_EMAIL`: Email address for property owner notifications
- `IMAP_HOST`: IMAP server hostname
- `IMAP_PORT`: IMAP server port (default: 993)
- `SCHEDULE_INTERVAL`: Lambda execution schedule (e.g., "rate(15 minutes)")
- `AWS_REGION`: AWS region for deployment (e.g., "us-east-1")

### Property Configuration

Properties are configured in `config/properties.json`:

```json
{
  "properties": [
    {
      "platformId": {
        "airbnb": "AIRBNB_LISTING_ID",
        "vrbo": "VRBO_PROPERTY_ID",
        "bookingcom": "BOOKING_LISTING_ID"
      },
      "address": "123 Main St, City, State 12345",
      "cleaners": [
        {
          "name": "Cleaner Name",
          "email": "cleaner@example.com",
          "rank": 1
        }
      ],
      "metadata": {
        "propertyName": "Beach House",
        "bedrooms": 3,
        "cleaningDuration": "3 hours"
      }
    }
  ]
}
```

## Deployment

### Prerequisites

- .NET 10 SDK
- AWS CLI configured
- GitHub repository with Actions enabled

### Deploy to Dev

```bash
git push origin develop
```

### Deploy to Prod

```bash
git push origin main
```

Or create a release tag:

```bash
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0
```

## Development

### Build

```bash
dotnet build src/RentalTurnManager.sln
```

### Test

```bash
dotnet test src/RentalTurnManager.sln --collect:"XPlat Code Coverage"
```

### Local Testing

The Lambda function can be tested locally using the AWS SAM CLI or by running the unit tests.

## Workflow

1. **Email Scanning**: Lambda function runs on schedule, scans IMAP inbox
2. **Booking Detection**: Identifies new booking emails from supported platforms
3. **Property Lookup**: Matches booking to configured property
4. **Step Functions Execution**: Initiates cleaner coordination workflow
5. **Cleaner Contact**: Contacts first cleaner in ranked list
6. **Response Handling**: 
   - If "yes": Sends calendar invite to cleaner and owner
   - If "no" or timeout: Contacts next cleaner in list
7. **Escalation**: If all cleaners decline, notifies owner

## Message Templates

Message templates are configured in `config/message-templates.json` and can be customized per deployment environment.

## Monitoring

- CloudWatch Logs: Lambda execution logs
- CloudWatch Metrics: Lambda invocations, errors, duration
- Step Functions Console: Workflow execution history
- X-Ray: Distributed tracing (optional)

## Tags

All resources are tagged with:
- `Owner`: Property owner identifier
- `Description`: Resource description
- `Environment`: dev or prod
- `AppName`: RentalTurnManager

## License

MIT License - see LICENSE file for details

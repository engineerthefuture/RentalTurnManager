# Rental Turn Manager

A serverless AWS application for automating rental property turnover scheduling by monitoring booking emails and coordinating with cleaning staff.

## Overview

Rental Turn Manager automates the process of scheduling property cleanings when new bookings are received. It monitors an IMAP email account for booking confirmations from platforms like Airbnb, VRBO, and Booking.com, then automatically contacts preferred cleaners to schedule turnovers.

## Features

- **Email Monitoring**: Scans IMAP inbox for all booking confirmations on a scheduled basis
- **Multi-Platform Support**: Parses bookings from Airbnb, VRBO, and Booking.com
- **Smart Booking Tracking**: Uses S3 to track booking state and prevent duplicate processing
- **Change Detection**: Automatically detects booking modifications and re-triggers workflows
- **Automated Cleaner Coordination**: Contacts cleaners in priority order via email with confirm/deny links
- **Calendar Integration**: Sends ICS calendar invites with proper timezone handling to cleaners and owners
- **Property Configuration**: Maintains property metadata, addresses, and cleaner preferences
- **Multi-Environment**: Supports dev and prod deployments with GitHub Actions
- **Secure Credentials**: Uses AWS Secrets Manager for email credentials

## Architecture

### Components

1. **Main Lambda (C# .NET 10)**: Scheduled function that scans IMAP inbox, parses bookings, checks for changes, and triggers workflows
2. **Calendar Lambda (C# .NET 10)**: Generates and sends ICS calendar invites with timezone support via Amazon SES
3. **Callback Lambda (C# .NET 10)**: HTTP endpoint that receives cleaner responses and signals Step Functions
4. **Step Functions Workflow**: Orchestrates the cleaner coordination process with wait states and callbacks
5. **S3 Bucket**: Stores booking state as JSON files organized by platform and confirmation code
6. **CloudFormation Stack**: Infrastructure as code defining all AWS resources
7. **GitHub Actions**: CI/CD pipeline with OIDC authentication

### AWS Services Used

- **AWS Lambda** (C# .NET 10): Email scanning, booking parsing, calendar generation, callback handling
- **AWS Step Functions**: Workflow orchestration with task callbacks
- **Amazon S3**: Booking state persistence and change tracking
- **AWS Secrets Manager**: Secure email credential storage
- **Amazon EventBridge**: Scheduled Lambda invocations (configurable interval)
- **Amazon SES**: Outbound email sending (cleaner notifications, calendar invites)
- **API Gateway HTTP**: RESTful callback endpoint for cleaner responses
- **CloudWatch Logs**: Centralized logging and monitoring
- **AWS CloudFormation**: Infrastructure as code deployment

## Project Structure

```
RentalTurnManager/
├── src/
│   ├── RentalTurnManager.Lambda/          # Main email scanning Lambda
│   ├── RentalTurnManager.CalendarLambda/  # Calendar invite generator
│   ├── RentalTurnManager.CallbackLambda/  # Cleaner response handler
│   ├── RentalTurnManager.Core/            # Core business logic and services
│   │   └── Services/                      # Email scanner, booking parser, state management
│   ├── RentalTurnManager.Models/          # Data models and DTOs
│   └── RentalTurnManager.Tests/           # Unit tests with xUnit
├── infrastructure/
│   ├── cloudformation/
│   │   ├── main.yaml                      # CloudFormation template (all resources)
│   │   └── parameters/
│   │       ├── dev.json                   # Dev environment parameters
│   │       └── prod.json                  # Prod environment parameters
│   └── stepfunctions/
│       └── cleaner-workflow.json          # Step Functions state machine definition
├── .github/
│   └── workflows/
│       └── deploy.yml                     # CI/CD pipeline (build, test, deploy)
├── config/
│   ├── properties.json                    # Property configurations (generated from GitHub variable)
│   └── message-templates.json             # Email message templates
└── README.md
```

## Setup

### Prerequisites

1. **AWS Account**: Active AWS account with appropriate permissions
2. **.NET 10 SDK**: Install from https://dotnet.microsoft.com/download
3. **GitHub Account**: For hosting code and running CI/CD
4. **IMAP Email Account**: Gmail, iCloud, or other IMAP-enabled email provider

### Initial Configuration

#### 1. GitHub Secrets

Navigate to your GitHub repository → Settings → Secrets and variables → Actions

Add the following **secrets**:

- `EMAIL_USERNAME`: IMAP email account username
- `EMAIL_PASSWORD`: IMAP email account password (use app-specific password for Gmail/iCloud)

#### 2. GitHub Variables

Add the following **variables**:

**Required:**
- `AWS_REGION`: AWS region (e.g., `us-east-1`)
- `AWS_ACCOUNT_ID`: Your AWS account ID (12-digit number)
- `OIDC_ROLE_NAME`: `GitHubActionsOIDCRole` (IAM role for GitHub Actions)
- `OWNER_EMAIL`: Property owner email address
- `IMAP_HOST`: IMAP server hostname (e.g., `imap.gmail.com`, `imap.mail.me.com`)
- `PROPERTIES_CONFIG`: JSON string with property configurations (see below)

**Optional (with defaults):**
- `NAMESPACE_PREFIX`: Resource name prefix (default: `bf`)
- `OWNER_NAME`: Property owner name (default: `Property Owner`)
- `IMAP_PORT`: IMAP port (default: `993`)
- `SCHEDULE_INTERVAL`: Lambda schedule (default: `rate(15 minutes)`)
- `APP_NAME`: Application name (default: `RentalTurnManager`)
- `APP_DESCRIPTION`: Application description (default: `Rental property turnover management system`)

#### 3. Properties Configuration

Create a GitHub variable called `PROPERTIES_CONFIG` with your rental property configuration as a JSON string:

```json
{
  "properties": [
    {
      "propertyId": "unique-property-id",
      "platformIds": {
        "airbnb": "YOUR_AIRBNB_LISTING_ID",
        "vrbo": "YOUR_VRBO_PROPERTY_ID",
        "bookingcom": "YOUR_BOOKING_COM_ID"
      },
      "address": "123 Main St, City, State 12345",
      "cleaners": [
        {
          "name": "Primary Cleaner",
          "email": "cleaner1@example.com",
          "phone": "+1-555-0100",
          "rank": 1
        },
        {
          "name": "Backup Cleaner",
          "email": "cleaner2@example.com",
          "phone": "+1-555-0200",
          "rank": 2
        }
      ],
      "metadata": {
        "propertyName": "Beach House",
        "bedrooms": 3,
        "bathrooms": 2,
        "cleaningDuration": "3 hours",
        "accessInstructions": "Lockbox code: 1234",
        "specialInstructions": "Clean refrigerator thoroughly"
      }
    }
  ],
  "emailFilters": {
    "bookingPlatformFromAddresses": ["airbnb.com", "vrbo.com", "booking.com"],
    "subjectPatterns": ["Reservation confirmed", "Instant Booking from", "booking confirmation"]
  }
}
```

**Note**: Store this as a single-line JSON string in the GitHub variable. The deployment workflow will write it to `config/properties.json`.

#### 4. Email Provider Setup

**For Gmail:**
1. Enable IMAP in Gmail settings
2. Enable 2-Step Verification
3. Create an App Password: Google Account → Security → App passwords
4. Use the app password as `EMAIL_PASSWORD`

**For iCloud:**
1. Enable IMAP in iCloud Mail settings
2. Generate an app-specific password at appleid.apple.com
3. Use `imap.mail.me.com` as `IMAP_HOST`

**For Other Providers:**
- Verify IMAP is enabled
- Use correct host and port (usually 993 for SSL)
- May require app-specific passwords

#### 5. AWS SES Configuration

Configure Amazon SES to send emails:

```bash
# Verify sender email (owner)
aws ses verify-email-identity --email-address your-owner@example.com --region us-east-1

# If in SES sandbox mode, also verify recipient emails
aws ses verify-email-identity --email-address cleaner@example.com --region us-east-1

# Request production access (optional - removes sandbox restrictions)
# AWS Console → SES → Account dashboard → Request production access
```

## Deployment

Deployment is fully automated via GitHub Actions using OIDC authentication (no long-lived AWS credentials required).

### Deploy to Dev Environment

```bash
# Push to develop branch
git checkout develop
git push origin develop
```

This triggers the CI/CD pipeline which:
1. Builds the .NET solution
2. Runs all unit tests
3. Packages Lambda functions
4. Deploys CloudFormation stack to dev environment

### Deploy to Production

```bash
# Merge to main branch
git checkout main
git merge develop
git push origin main

# Or create a release tag
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0
```

### Manual Deployment (Optional)

If you need to deploy manually:

```bash
# Build and package
dotnet build src/RentalTurnManager.sln --configuration Release
dotnet publish src/RentalTurnManager.Lambda --configuration Release -o ./publish/main
dotnet publish src/RentalTurnManager.CalendarLambda --configuration Release -o ./publish/calendar
dotnet publish src/RentalTurnManager.CallbackLambda --configuration Release -o ./publish/callback

# Package for Lambda
cd publish/main && zip -r ../../lambda-main.zip . && cd ../..
cd publish/calendar && zip -r ../../lambda-calendar.zip . && cd ../..
cd publish/callback && zip -r ../../lambda-callback.zip . && cd ../..

# Deploy CloudFormation stack
aws cloudformation deploy \
  --template-file infrastructure/cloudformation/main.yaml \
  --stack-name RentalTurnManager-dev \
  --parameter-overrides file://infrastructure/cloudformation/parameters/dev.json \
  --capabilities CAPABILITY_NAMED_IAM
```

## Development

### Local Setup

```bash
# Clone repository
git clone https://github.com/YOUR_USERNAME/RentalTurnManager.git
cd RentalTurnManager

# Restore dependencies
dotnet restore src/RentalTurnManager.sln

# Build solution
dotnet build src/RentalTurnManager.sln
```

### Testing

```bash
# Run all tests
dotnet test src/RentalTurnManager.sln

# Run with code coverage
dotnet test src/RentalTurnManager.sln --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter FullyQualifiedName~BookingParserServiceTests

# Run in watch mode for development
dotnet watch test --project src/RentalTurnManager.Tests
```

### Code Standards

- **Style**: Follow Microsoft C# coding conventions
- **Documentation**: Add XML comments for public APIs
- **Testing**: Maintain 80%+ code coverage
- **Commits**: Use conventional commit format (`feat:`, `fix:`, `docs:`, etc.)
- **File Headers**: All files include header comments with purpose, author, and date

### Running Locally

```bash
# Test Lambda with SAM CLI
sam local invoke EmailScannerLambda --event test-event.json

# Or manually invoke deployed Lambda
aws lambda invoke \
  --function-name RentalTurnManager-EmailScanner-dev \
  --payload '{"forceRescan":false}' \
  response.json && cat response.json
```

## How It Works

### Email Processing Workflow

1. **Scheduled Execution**: EventBridge triggers Main Lambda on configured interval (default: every 15 minutes)
2. **Email Scanning**: Lambda connects to IMAP inbox and retrieves all booking emails (not just unread)
3. **Booking Parsing**: 
   - Extracts confirmation codes (e.g., `HMFMAQS9MB` for Airbnb)
   - Parses dates, guest counts (adults + children), property IDs
   - Handles multiple email formats from Airbnb, VRBO, Booking.com
4. **State Management**:
   - Checks S3 for existing booking state (`bookings/{platform}/{confirmationCode}.json`)
   - Compares booking details to detect changes (dates, guests, property)
   - Skips unchanged bookings to prevent duplicate workflows
5. **Property Matching**: Looks up property configuration using platform-specific listing IDs
6. **Workflow Trigger**: Starts Step Functions workflow with booking and property details

### Cleaner Coordination Workflow

1. **Initial Contact**: Step Functions sends email to highest-ranked cleaner with confirm/deny links
2. **Callback Wait**: Workflow pauses using task token, waiting for HTTP callback from cleaner
3. **Response Processing**:
   - **Confirmed**: Calendar Lambda generates ICS invites for cleaner and owner (12:00 PM EST on checkout day)
   - **Declined**: Workflow contacts next cleaner in ranked list
   - **Timeout**: After configured period, moves to next cleaner
4. **Calendar Invites**: 
   - Includes property address, guest details, cleaning duration
   - Adds cleaner as required participant, owner as optional participant
   - Proper timezone conversion (Eastern Time)
   - Sent via Amazon SES with raw MIME format
5. **Escalation**: If all cleaners decline or timeout, notifies property owner

### Booking Change Detection

The system tracks booking state in S3 to handle:
- **New Bookings**: Triggers workflow immediately
- **Modified Bookings**: Re-triggers workflow if dates, guests, or property change
- **Unchanged Bookings**: Skips processing to avoid duplicate notifications
- **Cancellations**: Can be extended to handle cancellation detection

## Monitoring & Troubleshooting

### CloudWatch Logs

```bash
# View Main Lambda logs
aws logs tail /aws/lambda/RentalTurnManager-EmailScanner-dev --follow

# View Calendar Lambda logs
aws logs tail /aws/lambda/RentalTurnManager-Calendar-dev --follow

# View Callback Lambda logs
aws logs tail /aws/lambda/RentalTurnManager-Callback-dev --follow
```

### Key Log Messages

- `Extracted booking reference: HMFMAQS9MB` - Confirmation code parsed successfully
- `Booking missing reference ID` - Parser couldn't extract confirmation code (check email format)
- `Booking unchanged, skipping workflow` - No changes detected, workflow not triggered
- `Processing new or updated booking` - Changes detected, workflow will start
- `No property configuration found` - Property ID doesn't match any configured properties

### CloudWatch Metrics

Access through AWS Console:
- **Lambda**: Functions → {FunctionName} → Monitoring
- **Step Functions**: State machines → {StateMachineName} → Monitoring
- **API Gateway**: APIs → {ApiName} → Monitoring

### Common Issues

**Issue**: Lambda can't access Secrets Manager  
**Solution**: Check IAM role permissions in CloudFormation template, verify secret exists

**Issue**: Email scanning not finding bookings  
**Solutions**:
- Verify IMAP credentials in Secrets Manager
- Check email subject patterns in properties config
- Review parser regex patterns in BookingParserService.cs
- Enable debug logging and check CloudWatch Logs

**Issue**: Bookings saved as "confirmed.json" instead of actual confirmation code  
**Solution**: Enhanced regex patterns now specifically look for codes like `HM[A-Z0-9]{8-10}`

**Issue**: Calendar invites showing wrong time  
**Solution**: Fixed - now properly converts to Eastern Time (12:00 PM EST)

**Issue**: Guest count incorrect  
**Solution**: Parser now correctly handles "X adults, Y children" format

**Issue**: Property not found for booking  
**Solutions**:
- Verify platform IDs in properties config match exactly (case-sensitive)
- Check logs for "Parsed booking: {platform} - {propertyId}"
- Review available properties in error message

**Issue**: Cleaners not receiving emails  
**Solutions**:
- Verify SES sender email is verified
- Check if SES is in sandbox mode (verify all recipient emails)
- Verify cleaner emails in properties configuration
- Check Step Functions execution history for errors

### Step Functions Debugging

View execution history in AWS Console:
1. Step Functions → State machines → RentalTurnManager-CleanerWorkflow-{env}
2. Click on specific execution
3. Review step-by-step execution with inputs/outputs
4. Check for failed states or timeouts

## Cost Estimation

Approximate monthly costs (based on 1 property, checking every 15 minutes):

| Service | Usage | Monthly Cost |
|---------|-------|--------------|
| Lambda (Main) | ~2,880 invocations/month @ 1s avg | $0.60 |
| Lambda (Calendar) | ~20 invocations/month @ 0.5s avg | $0.01 |
| Lambda (Callback) | ~20 invocations/month @ 0.2s avg | $0.01 |
| Step Functions | ~20 executions, 100 state transitions | $2.50 |
| S3 | Storage + requests | $0.10 |
| Secrets Manager | 1 secret | $0.40 |
| SES | ~100 emails/month | $0.00 (free tier) |
| CloudWatch Logs | ~5 GB/month | $2.50 |
| API Gateway | ~20 requests/month | $0.00 (free tier) |

**Estimated Total**: $6-8/month per property

**Scaling**: Add ~$3-5/month for each additional property.

## Contributing

### Development Workflow

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature-name`
3. Make changes and add tests
4. Ensure all tests pass: `dotnet test`
5. Commit with conventional format: `feat: Add new feature`
6. Push and create pull request

### Adding New Booking Platforms

To support a new platform (e.g., HomeAway):

1. Update `BookingParserService.cs`:
   - Add platform detection in `DeterminePlatform()`
   - Create `ParseHomeAwayBooking()` method
   - Add regex patterns for confirmation codes and dates
2. Update properties configuration to include new platform ID mapping
3. Add comprehensive unit tests
4. Update documentation

### Pull Request Guidelines

- Clear title and description
- Link related issues
- All tests passing (21/21)
- Code coverage maintained above 80%
- Update README if adding features

## Resource Tagging

All AWS resources are tagged with:
- `Owner`: Property owner identifier
- `Description`: Resource description  
- `Environment`: `dev` or `prod`
- `AppName`: `RentalTurnManager`
- `ManagedBy`: `CloudFormation`

## Security

- Email credentials stored in AWS Secrets Manager (encrypted at rest)
- OIDC authentication for GitHub Actions (no long-lived AWS credentials)
- IAM roles follow least-privilege principle
- Lambda functions run with minimal required permissions
- API Gateway callback endpoint is public but validated with task tokens
- S3 bucket has server-side encryption enabled
- All traffic uses HTTPS/TLS

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Author

**Brent Foster**  
Created: January 11, 2026

## Quick Reference

### Common Commands

```bash
# Build & Test
dotnet build src/RentalTurnManager.sln
dotnet test src/RentalTurnManager.sln
dotnet test --collect:"XPlat Code Coverage"

# Package Lambda functions
cd src/RentalTurnManager.Lambda && dotnet publish -c Release -o ../../publish/main
cd src/RentalTurnManager.CalendarLambda && dotnet publish -c Release -o ../../publish/calendar
cd src/RentalTurnManager.CallbackLambda && dotnet publish -c Release -o ../../publish/callback

# Manual Lambda invocation
aws lambda invoke \
  --function-name RentalTurnManager-EmailScanner-dev \
  --payload '{"forceRescan":false}' \
  response.json

# View CloudWatch logs
aws logs tail /aws/lambda/RentalTurnManager-EmailScanner-dev --follow

# Describe Step Functions execution
aws stepfunctions list-executions \
  --state-machine-arn $(aws cloudformation describe-stacks \
    --stack-name RentalTurnManager-dev \
    --query 'Stacks[0].Outputs[?OutputKey==`CleanerWorkflowStateMachineArn`].OutputValue' \
    --output text)

# Update stack parameters
aws cloudformation update-stack \
  --stack-name RentalTurnManager-dev \
  --use-previous-template \
  --parameters ParameterKey=ScheduleInterval,ParameterValue="rate(30 minutes)" \
  --capabilities CAPABILITY_NAMED_IAM
```

### GitHub Actions Triggers

```bash
# Deploy to dev
git push origin develop

# Deploy to prod
git push origin main

# Create versioned release
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

### Key Environment Variables

Set by CloudFormation and available in Lambda:

- `ENVIRONMENT`: `dev` or `prod`
- `EMAIL_SECRET_NAME`: Secrets Manager secret ARN
- `CLEANER_WORKFLOW_STATE_MACHINE_ARN`: Step Functions ARN
- `BOOKING_STATE_BUCKET`: S3 bucket for booking state
- `OWNER_EMAIL`: Property owner email
- `OWNER_NAME`: Property owner name
- `IMAP_HOST`: IMAP server hostname
- `IMAP_PORT`: IMAP server port
- `PROPERTIES_CONFIG`: JSON property configuration
- `CALLBACK_API_URL`: API Gateway callback endpoint

## Support

For issues, questions, or feature requests:
- Open an issue on GitHub
- Review existing issues and discussions
- Check CloudWatch Logs for debugging information

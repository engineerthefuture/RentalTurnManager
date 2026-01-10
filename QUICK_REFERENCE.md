# RentalTurnManager - Quick Reference

## Project Structure

```
rental-turn-manager/
├── .github/workflows/
│   └── deploy.yml                          # CI/CD pipeline
├── config/
│   ├── properties.json                     # Property configurations
│   └── message-templates.json              # Email templates
├── infrastructure/
│   ├── cloudformation/
│   │   ├── main.yaml                       # Main CloudFormation template
│   │   └── parameters/
│   │       ├── dev.json                    # Dev environment parameters
│   │       └── prod.json                   # Prod environment parameters
│   └── stepfunctions/
│       └── cleaner-workflow.json           # Step Functions state machine
├── src/
│   ├── RentalTurnManager.Lambda/           # Lambda function
│   ├── RentalTurnManager.Core/             # Core services
│   ├── RentalTurnManager.Models/           # Data models
│   └── RentalTurnManager.Tests/            # Unit tests
├── CONTRIBUTING.md
├── LICENSE
├── README.md
└── SETUP.md
```

## Common Commands

### Build & Test
```bash
# Build solution
dotnet build src/RentalTurnManager.sln

# Run tests
dotnet test src/RentalTurnManager.sln

# Run tests with coverage
dotnet test src/RentalTurnManager.sln --collect:"XPlat Code Coverage"

# Build Lambda package
cd src/RentalTurnManager.Lambda
dotnet lambda package --configuration Release --output-package ../../lambda.zip
```

### AWS Operations
```bash
# Deploy CloudFormation stack
aws cloudformation create-stack \
  --stack-name RentalTurnManager-dev \
  --template-body file://infrastructure/cloudformation/main.yaml \
  --parameters file://infrastructure/cloudformation/parameters/dev.json \
  --capabilities CAPABILITY_NAMED_IAM

# Update Lambda function
aws lambda update-function-code \
  --function-name RentalTurnManager-EmailScanner-dev \
  --zip-file fileb://lambda.zip

# Invoke Lambda manually
aws lambda invoke \
  --function-name RentalTurnManager-EmailScanner-dev \
  --payload '{}' \
  response.json

# View logs
aws logs tail /aws/lambda/RentalTurnManager-EmailScanner-dev --follow

# Describe Step Functions execution
aws stepfunctions describe-execution \
  --execution-arn "EXECUTION_ARN"
```

### GitHub Actions
```bash
# Deploy to dev
git push origin develop

# Deploy to prod
git push origin main

# Create release
git tag -a v1.0.0 -m "Release v1.0.0"
git push origin v1.0.0
```

## Key Components

### Lambda Function
- **File**: `src/RentalTurnManager.Lambda/Function.cs`
- **Purpose**: Scans IMAP inbox for booking emails
- **Trigger**: EventBridge scheduled rule
- **Output**: Starts Step Functions workflows

### Core Services

1. **SecretsService**: Retrieves email credentials from AWS Secrets Manager
2. **EmailScannerService**: Connects to IMAP and scans for booking emails
3. **BookingParserService**: Parses booking information from emails
4. **PropertyConfigService**: Manages property configurations
5. **StepFunctionService**: Starts cleaner coordination workflows

### Step Functions Workflow
- **File**: `infrastructure/stepfunctions/cleaner-workflow.json`
- **Purpose**: Coordinates cleaner availability checks
- **Flow**:
  1. Contact first cleaner in list
  2. Wait for response
  3. If "yes": Send confirmation and notify owner
  4. If "no": Contact next cleaner
  5. If all decline: Escalate to owner

## Configuration Reference

### GitHub Secrets (Required)
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `EMAIL_USERNAME`
- `EMAIL_PASSWORD`

### GitHub Variables (Required)
- `AWS_REGION`
- `OWNER_EMAIL`
- `IMAP_HOST`
- `IMAP_PORT`
- `SCHEDULE_INTERVAL`

### Environment Variables (Set by CloudFormation)
- `ENVIRONMENT`: dev or prod
- `EMAIL_SECRET_NAME`: Secrets Manager secret name
- `CLEANER_WORKFLOW_STATE_MACHINE_ARN`: Step Functions ARN
- `OWNER_EMAIL`: Property owner email
- `IMAP_HOST`: IMAP server host
- `IMAP_PORT`: IMAP server port

## Workflow Overview

1. **Scheduled Trigger**: EventBridge triggers Lambda every N minutes
2. **Scan Emails**: Lambda connects to IMAP and retrieves booking emails
3. **Parse Bookings**: Extracts booking details from emails
4. **Lookup Property**: Matches booking to configured property
5. **Start Workflow**: Initiates Step Functions for cleaner coordination
6. **Contact Cleaners**: Step Functions contacts cleaners in rank order
7. **Confirmation**: Sends calendar invite and notifications on confirmation
8. **Escalation**: Notifies owner if no cleaner available

## Supported Platforms

- **Airbnb**: Parses reservation confirmations
- **VRBO**: Parses booking confirmations
- **Booking.com**: Parses reservation confirmations

## Email Template Variables

Available in `config/message-templates.json`:

- `{CleanerName}`: Cleaner's name
- `{PropertyName}`: Property friendly name
- `{Date}`: Cleaning date
- `{Time}`: Cleaning time
- `{Address}`: Property address
- `{CleaningDuration}`: Estimated duration
- `{AccessInstructions}`: Property access info
- `{SpecialInstructions}`: Special cleaning notes
- `{Platform}`: Booking platform (airbnb, vrbo, bookingcom)
- `{BookingReference}`: Booking confirmation number
- `{GuestName}`: Guest name
- `{CheckInDate}`: Guest check-in date
- `{CheckoutDate}`: Guest checkout date
- `{CleanerCount}`: Number of cleaners contacted
- `{CleanerList}`: List of cleaners contacted

## Troubleshooting Quick Checks

### Lambda not running?
- Check EventBridge rule is enabled
- Verify Lambda has correct IAM permissions
- Check CloudWatch Logs for errors

### Emails not being scanned?
- Verify IMAP credentials in Secrets Manager
- Check email account has IMAP enabled
- Verify email filters in EmailScannerService

### Properties not matching?
- Check platform IDs in `config/properties.json`
- Verify booking parser extracts correct property ID
- Review booking email format

### Cleaners not receiving emails?
- Verify SES email address is verified
- Check SES is not in sandbox (or recipients are verified)
- Review Step Functions execution logs

### Deployment failing?
- Check GitHub secrets are set correctly
- Verify AWS credentials have necessary permissions
- Review GitHub Actions logs

## Security Best Practices

1. **Never commit secrets**: Use GitHub Secrets and AWS Secrets Manager
2. **Use least privilege**: IAM roles have minimum required permissions
3. **Enable encryption**: S3 buckets and Secrets Manager use encryption
4. **Regular updates**: Keep NuGet packages updated
5. **Monitor access**: Use CloudTrail to audit AWS API calls

## Performance Tuning

- **Lambda Memory**: Default 512MB, adjust based on usage
- **Lambda Timeout**: Default 300s (5 min), adjust if needed
- **Schedule Interval**: Balance between responsiveness and cost
- **Email Batch Size**: Consider pagination for large inboxes
- **Step Functions Timeout**: Adjust wait times for cleaner responses

## Cost Optimization

- Use appropriate schedule interval (15-30 minutes)
- Implement email deduplication
- Set proper log retention periods (30 days default)
- Use SES free tier (10,000 emails/month)
- Consider Reserved Concurrency for Lambda if predictable load

## Support Resources

- **AWS Documentation**: https://docs.aws.amazon.com/
- **.NET on AWS**: https://aws.amazon.com/developer/language/net/
- **MailKit Documentation**: http://www.mimekit.net/docs/
- **GitHub Issues**: For bug reports and feature requests

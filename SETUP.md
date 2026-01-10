# Setup Guide

## Prerequisites

1. **AWS Account**: Active AWS account with appropriate permissions
2. **.NET 10 SDK**: Install from https://dotnet.microsoft.com/download
3. **AWS CLI**: Install and configure with your credentials
4. **GitHub Account**: For hosting code and CI/CD

## Initial Setup

### 1. Configure GitHub Secrets

Navigate to your GitHub repository → Settings → Secrets and variables → Actions

Add the following **secrets**:

- `EMAIL_USERNAME`: IMAP email username
- `EMAIL_PASSWORD`: IMAP email password

### 2. Configure GitHub Variables

Add the following **variables**:

- `AWS_REGION`: AWS region (e.g., `us-east-1`)
- `AWS_ACCOUNT_ID`: Your AWS account ID (12-digit number)
- `OIDC_ROLE_NAME`: GitHubActionsOIDCRole
- `NAMESPACE_PREFIX`: Namespace prefix for AWS resources - Optional, defaults to "bf"
- `OWNER_EMAIL`: Property owner email address
- `OWNER_NAME`: Property owner name - Optional, defaults to "Property Owner"
- `PROPERTIES_CONFIG`: JSON configuration for your rental properties (see Properties Configuration section below)
- `IMAP_HOST`: IMAP server hostname (e.g., `imap.mail.me.com` or `imap.gmail.com`)
- `IMAP_PORT`: IMAP port (usually `993`) - Optional, defaults to 993
- `SCHEDULE_INTERVAL`: Lambda schedule expression (e.g., `rate(15 minutes)`) - Optional, defaults to rate(15 minutes)
- `APP_NAME`: Application name - Optional, defaults to RentalTurnManager
- `APP_DESCRIPTION`: Application description - Optional, defaults to "Rental property turnover management system"


### 3. Properties Configuration

Create a GitHub variable called `PROPERTIES_CONFIG` with your rental property configuration as a JSON string. This keeps your property details private and out of the repository.

**Format:**

```json
{
  "properties": [
    {
      "propertyId": "unique-id",
      "platformIds": {
        "airbnb": "YOUR_AIRBNB_LISTING_ID",
        "vrbo": "YOUR_VRBO_PROPERTY_ID",
        "bookingcom": "YOUR_BOOKING_LISTING_ID"
      },
      "address": "Property Address",
      "cleaners": [
        {
          "name": "Cleaner Name",
          "email": "cleaner@example.com",
          "phone": "+1-555-0100",
          "rank": 1
        }
      ],
      "metadata": {
        "propertyName": "Friendly Name",
        "bedrooms": 3,
        "bathrooms": 2,
        "cleaningDuration": "3 hours",
        "accessInstructions": "Access instructions",
        "specialInstructions": "Special cleaning notes",
        "ownerName": "Owner Name"
      }
    }
  ]
}
```

**Note:** Store the entire JSON as a single-line string in the GitHub variable. The workflow will write this to `config/properties.json` during deployment.

### 4. Email Setup

#### For Gmail:
1. Enable IMAP in Gmail settings
2. Create an App Password (not your regular password)
   - Go to Google Account → Security → 2-Step Verification → App passwords
3. Use the app password as `EMAIL_PASSWORD`

#### For Other Providers:
- Ensure IMAP is enabled
- Use appropriate host and port settings
- May need to allow "less secure apps" or create app-specific passwords

### 5. AWS SES Configuration

For sending emails, you need to configure Amazon SES:

```bash
# Verify your sender email address
aws ses verify-email-identity --email-address owner@example.com

# If in sandbox, verify recipient emails too
aws ses verify-email-identity --email-address cleaner@example.com

# Request production access if needed (removes sandbox restrictions)
# Do this through AWS Console → SES → Account Dashboard → Request Production Access
```

### 6. Deploy to Dev Environment

```bash
# Push to develop branch to trigger deployment
git checkout -b develop
git push origin develop
```

### 7. Deploy to Production

```bash
# Merge to main or create a release tag
git checkout main
git merge develop
git push origin main

# Or create a release
git tag -a v1.0.0 -m "Initial release"
git push origin v1.0.0
```

## Testing

### Local Testing

```bash
# Build the solution
dotnet build src/RentalTurnManager.sln

# Run tests
dotnet test src/RentalTurnManager.sln

# Check code coverage
dotnet test src/RentalTurnManager.sln --collect:"XPlat Code Coverage"
```

### Test Lambda Locally

Using AWS SAM CLI:

```bash
sam local invoke EmailScannerLambda --event test-event.json
```

### Manual Lambda Invocation

```bash
aws lambda invoke \
  --function-name RentalTurnManager-EmailScanner-dev \
  --payload '{"forceRescan":false}' \
  response.json

cat response.json
```

## Monitoring

### CloudWatch Logs

```bash
# View Lambda logs
aws logs tail /aws/lambda/RentalTurnManager-EmailScanner-dev --follow

# View Step Functions logs
aws logs tail /aws/stepfunctions/RentalTurnManager-CleanerWorkflow-dev --follow
```

### CloudWatch Metrics

Access through AWS Console:
- Lambda → Functions → RentalTurnManager-EmailScanner-{env} → Monitoring
- Step Functions → State machines → RentalTurnManager-CleanerWorkflow-{env} → Monitoring

## Troubleshooting

### Issue: Lambda can't access Secrets Manager

**Solution**: Check IAM role permissions in CloudFormation template

### Issue: Email scanning not finding bookings

**Solutions**:
1. Verify IMAP credentials in Secrets Manager
2. Check email filters aren't too restrictive
3. Verify booking email formats match parser patterns
4. Enable debug logging and check CloudWatch Logs

### Issue: No cleaners receiving emails

**Solutions**:
1. Verify SES sender email is verified
2. Check if SES is in sandbox mode (limits recipients)
3. Verify cleaner emails in properties configuration
4. Check Step Functions execution history for errors

### Issue: Property not found for booking

**Solutions**:
1. Verify platform IDs in `config/properties.json` match exactly
2. Check booking parser is extracting correct property ID
3. Review CloudWatch logs for property lookup attempts

## Updating Configuration

### Update Properties

1. Edit `config/properties.json`
2. Commit and push changes
3. GitHub Actions will deploy updated configuration

### Update Message Templates

1. Edit `config/message-templates.json`
2. Commit and push changes
3. GitHub Actions will deploy updated templates

### Update Schedule

Update GitHub variable `SCHEDULE_INTERVAL` and redeploy, or update CloudFormation parameters directly:

```bash
aws cloudformation update-stack \
  --stack-name RentalTurnManager-dev \
  --use-previous-template \
  --parameters ParameterKey=ScheduleInterval,ParameterValue="rate(30 minutes)" \
  --capabilities CAPABILITY_NAMED_IAM
```

## Cost Estimation

Approximate monthly costs for moderate usage:

- **Lambda**: ~$1-5 (based on 2,880 invocations/month at 15-min intervals)
- **Step Functions**: ~$1-3 (based on state transitions)
- **Secrets Manager**: ~$0.40 (per secret)
- **SES**: ~$0 (10,000 free emails/month)
- **CloudWatch Logs**: ~$0.50-2 (based on log volume)
- **S3**: ~$0.10 (minimal storage for deployment artifacts)

**Estimated Total**: $3-11/month

## Support

For issues, questions, or contributions, please open an issue on GitHub.

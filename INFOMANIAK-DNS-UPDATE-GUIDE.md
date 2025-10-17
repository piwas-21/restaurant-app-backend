# Infomaniak DNS Configuration Update Guide

## Overview
This guide provides step-by-step instructions for updating DNS records on Infomaniak when AWS EKS cluster changes occur. The DNS records need to be updated to point to the new Application Load Balancer (ALB) endpoint created by the AWS Load Balancer Controller.

## When to Update DNS
DNS updates are required when:
- A new EKS cluster is provisioned
- The ALB Ingress Controller creates a new load balancer
- The cluster is migrated to a different AWS region
- The ALB endpoint changes due to infrastructure updates

## Understanding the Architecture

### How ALB is Created
The ALB endpoint is **automatically created** by the AWS Load Balancer Controller when you apply the Ingress resource. You don't need to manually configure the ALB endpoint in your Kubernetes manifests.

The Ingress configuration in `base/ingress.yaml` uses annotations that tell the AWS Load Balancer Controller to create an internet-facing ALB:

```yaml
annotations:
  alb.ingress.kubernetes.io/scheme: internet-facing
  alb.ingress.kubernetes.io/target-type: ip
  alb.ingress.kubernetes.io/certificate-arn: arn:aws:acm:eu-central-1:670079155071:certificate/ba079074-05de-49e4-b8d5-a7a75e6ac82a
```

### DNS Flow
```
User Request (rumirestaurant.ch)
         ↓
  Infomaniak DNS
         ↓
    AWS ALB (k8s-rumi-rumirest-*.elb.amazonaws.com)
         ↓
  Kubernetes Ingress Controller
         ↓
  Backend/Frontend Services
```

## Prerequisites

### Required Access
1. **Infomaniak Account Access**
   - Login credentials for Infomaniak admin panel
   - Domain management permissions for `rumirestaurant.ch`

2. **AWS/Kubernetes Access** (to retrieve ALB endpoint)
   - AWS credentials (provided by DevOps)
   - kubectl access to the cluster
   - jq installed for JSON parsing

## Step 1: Get the New ALB Endpoint

### Method 1: From DevOps Team
The DevOps team should provide the ALB endpoint when they provision a new cluster. It will look like:
```
k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com
```

### Method 2: Retrieve from Kubernetes
If you need to find it yourself:

1. **Configure AWS credentials:**
```bash
# Set base credentials
export AWS_ACCESS_KEY_ID=AKIAZYA6UPN7SKL4UXOL
export AWS_SECRET_ACCESS_KEY=RKyn6UdWhpm/mOHKq02j/oYm2njUUlx8KVPOjG/9
export AWS_DEFAULT_REGION=eu-central-1

# Assume the admin role
eval $(aws sts assume-role \
  --role-arn arn:aws:iam::670079155071:role/external-admin \
  --role-session-name rumi-dns-update | \
  jq -r '.Credentials | "export AWS_ACCESS_KEY_ID=\(.AccessKeyId)\nexport AWS_SECRET_ACCESS_KEY=\(.SecretAccessKey)\nexport AWS_SESSION_TOKEN=\(.SessionToken)\n"')
```

2. **Update kubeconfig:**
```bash
aws eks update-kubeconfig --name z2h-eks --region eu-central-1
```

3. **Get the Ingress ALB endpoint:**
```bash
kubectl get ingress rumi-restaurant-web -n rumi -o jsonpath='{.status.loadBalancer.ingress[0].hostname}'
```

This will output something like:
```
k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com
```

## Step 2: Update Infomaniak DNS Records

### Access Infomaniak DNS Manager
1. Log in to [Infomaniak Manager](https://manager.infomaniak.com/)
2. Navigate to **Domains** → **rumirestaurant.ch**
3. Click on **DNS Zone** or **Manage DNS**

### Current DNS Configuration Structure
The DNS zone for `rumirestaurant.ch` should have the following structure:

```dns
; Domain: rumirestaurant.ch
$TTL 3600
@                                      IN SOA   ns11.infomaniak.ch. hostmaster.infomaniak.ch. (SERIAL 10800 3600 605800 3600)
                                  3600 IN A     18.157.204.220
                                  3600 IN A     3.125.236.124
                                  3600 IN A     3.67.203.195
                                  3600 IN NS    ns11.infomaniak.ch.
                                  3600 IN NS    ns12.infomaniak.ch.
                                  3600 IN TXT   "v=spf1 -all"
www                               3600 IN CNAME <NEW_ALB_ENDPOINT>
_5d9dc1944bd4a3f8c0472e19593c8f5b 3600 IN CNAME _ec2593eada8be4845cd7c5068659b331.xlfgrmvvlj.acm-validations.aws.
_dmarc                            3600 IN TXT   "v=DMARC1; p=reject;"
```

### Records to Update

#### 1. Update WWW CNAME Record
**Current Record:**
```
Type: CNAME
Name: www
Value: k8s-rumi-rumirest-d55098070e-14220279.eu-central-1.elb.amazonaws.com.
TTL: 3600
```

**Update to:**
```
Type: CNAME
Name: www
Value: k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com.
TTL: 3600
```

**Note:** Always include the trailing dot (`.`) at the end of the CNAME value!

#### 2. Root Domain A Records
The root domain (`@` or `rumirestaurant.ch`) uses A records pointing to the ALB IP addresses. These IPs are resolved from the ALB endpoint.

**To get the current IPs:**
```bash
# Get IPs from the ALB endpoint
dig +short k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com
```

**Update the A records:**
```
Type: A
Name: @ (or leave empty for root)
Value: <IP_ADDRESS_1>
TTL: 3600
```

Add multiple A records for each IP address returned by the dig command.

**Current IPs (example):**
- 18.157.204.220
- 3.125.236.124
- 3.67.203.195

#### 3. ACM Certificate Validation (Usually unchanged)
The ACM validation CNAME should remain the same unless AWS Certificate Manager certificate is recreated:
```
Type: CNAME
Name: _5d9dc1944bd4a3f8c0472e19593c8f5b
Value: _ec2593eada8be4845cd7c5068659b331.xlfgrmvvlj.acm-validations.aws.
TTL: 3600
```

**⚠️ Important:** Only update this if AWS provides new ACM validation records!

### Step-by-Step Update Process in Infomaniak UI

1. **For WWW CNAME:**
   - Find the existing CNAME record for `www`
   - Click **Edit** or **Modify**
   - Replace the old ALB endpoint with the new one
   - Ensure the trailing dot is included
   - Save the changes

2. **For Root Domain A Records:**
   - Delete all existing A records for the root domain (`@`)
   - Add new A records for each IP address from the new ALB
   - Set TTL to 3600 (or as recommended)
   - Save the changes

3. **Verify Serial Number Update:**
   - The SOA record serial number should automatically increment
   - This ensures DNS propagation

## Step 3: Verify DNS Updates

### Check DNS Propagation

1. **Query Infomaniak nameservers directly:**
```bash
dig @ns11.infomaniak.ch rumirestaurant.ch A
dig @ns11.infomaniak.ch www.rumirestaurant.ch CNAME
```

2. **Check global propagation:**
```bash
# Check from your local DNS
dig rumirestaurant.ch
dig www.rumirestaurant.ch

# Check from multiple locations (optional)
# Use online tools like: https://www.whatsmydns.net/
```

3. **Verify CNAME resolution:**
```bash
# This should return the new ALB endpoint
dig www.rumirestaurant.ch CNAME +short
```

Expected output:
```
k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com.
```

4. **Verify end-to-end resolution:**
```bash
# This should return the IPs of the ALB
dig www.rumirestaurant.ch +short
```

### Test Application Access

1. **Test via curl:**
```bash
# Test HTTPS redirect
curl -I http://www.rumirestaurant.ch

# Test HTTPS access
curl -I https://www.rumirestaurant.ch

# Test backend API
curl -I https://www.rumirestaurant.ch/api/health
```

2. **Test in browser:**
   - Navigate to https://www.rumirestaurant.ch
   - Verify SSL certificate is valid
   - Check that both frontend and backend are accessible

## Step 4: Update Documentation

After successful DNS update, document the change:

1. **Update this file with new values:**
   - Update the example ALB endpoint
   - Update IP addresses if they changed
   - Update the timestamp

2. **Commit changes to git:**
```bash
cd /path/to/rumi-argocd-gitops
git add INFOMANIAK-DNS-UPDATE-GUIDE.md
git commit -m "docs: Update DNS configuration for new cluster migration"
# Don't push yet - review all changes first
```

3. **Notify the team:**
   - Inform the team about the DNS update
   - Provide the new ALB endpoint
   - Document any issues encountered

## Troubleshooting

### DNS Not Resolving

**Problem:** DNS queries return old IP addresses or NXDOMAIN

**Solutions:**
1. Check TTL - old records may be cached (default 3600s = 1 hour)
2. Clear local DNS cache:
   ```bash
   # macOS
   sudo dscacheutil -flushcache; sudo killall -HUP mDNSResponder
   
   # Linux
   sudo systemd-resolve --flush-caches
   ```
3. Wait for propagation (can take up to TTL duration)
4. Verify changes in Infomaniak panel are saved

### SSL Certificate Errors

**Problem:** Browser shows SSL certificate errors

**Solutions:**
1. Verify ACM certificate ARN in `ingress.yaml` matches the certificate
2. Check ACM certificate validation records in DNS
3. Ensure certificate covers both `rumirestaurant.ch` and `www.rumirestaurant.ch`
4. Wait for certificate validation (can take 5-30 minutes)

### ALB Not Responding

**Problem:** ALB endpoint doesn't respond or returns 503 errors

**Solutions:**
1. Check Ingress status:
   ```bash
   kubectl describe ingress rumi-restaurant-web -n rumi
   ```
2. Verify target groups have healthy targets:
   ```bash
   aws elbv2 describe-target-health --target-group-arn <TARGET_GROUP_ARN>
   ```
3. Check pod health:
   ```bash
   kubectl get pods -n rumi
   kubectl logs -n rumi <POD_NAME>
   ```

### CNAME vs A Record Confusion

**Problem:** Not sure whether to use CNAME or A record

**Solution:**
- **Root domain (`@` or `rumirestaurant.ch`)**: Must use A records (CNAME not allowed for root)
- **Subdomain (`www`)**: Use CNAME pointing to ALB endpoint
- AWS ALB uses multiple IPs, which is why root needs multiple A records

## DNS Configuration Checklist

Use this checklist when updating DNS:

- [ ] Obtained new ALB endpoint from DevOps or kubectl
- [ ] Logged into Infomaniak Manager
- [ ] Navigated to rumirestaurant.ch DNS zone
- [ ] Updated www CNAME record with new ALB endpoint (with trailing dot)
- [ ] Updated root domain A records with new ALB IPs
- [ ] Verified ACM validation CNAME is still correct
- [ ] Saved all changes in Infomaniak
- [ ] Verified DNS propagation with dig commands
- [ ] Tested HTTP to HTTPS redirect
- [ ] Tested frontend application access
- [ ] Tested backend API access (/api/health)
- [ ] Cleared browser cache and retested
- [ ] Updated documentation
- [ ] Notified team of changes

## Important Notes

### Things That DON'T Need Changes in GitOps

The following files do **NOT** need to be updated when the cluster changes:

1. **`base/ingress.yaml`** - The ALB endpoint is automatically created by AWS, not configured here
2. **`base/frontend-deployment.yaml`** - Deployment spec is cluster-agnostic
3. **`base/backend-deployment.yaml`** - Deployment spec is cluster-agnostic
4. **`overlays/prod/kustomization.yaml`** - No cluster-specific configuration

### What AWS Load Balancer Controller Does Automatically

The AWS Load Balancer Controller:
- Creates the ALB based on Ingress annotations
- Configures target groups for services
- Sets up health checks
- Manages SSL/TLS termination using ACM certificate
- Updates target group registrations as pods scale

### Security Considerations

1. **Never commit AWS credentials** to git repositories
2. **Rotate AWS credentials** regularly (provided by DevOps)
3. **Use IAM roles** with minimum required permissions
4. **Audit DNS changes** - keep a log of who changed what and when

## Quick Reference

### Latest Configuration (Updated: 2025-10-17)

**Current ALB Endpoint:**
```
k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com
```

**Current Root Domain IPs:**
- 18.157.204.220
- 3.125.236.124
- 3.67.203.195

**AWS Region:** eu-central-1  
**EKS Cluster:** z2h-eks  
**ACM Certificate ARN:** arn:aws:acm:eu-central-1:670079155071:certificate/ba079074-05de-49e4-b8d5-a7a75e6ac82a

### Useful Commands

```bash
# Get current ALB endpoint from Kubernetes
kubectl get ingress rumi-restaurant-web -n rumi -o jsonpath='{.status.loadBalancer.ingress[0].hostname}'

# Resolve ALB IPs
dig +short k8s-rumi-rumirest-d55098070e-1359988842.eu-central-1.elb.amazonaws.com

# Check DNS from Infomaniak nameservers
dig @ns11.infomaniak.ch www.rumirestaurant.ch

# Full DNS trace
dig +trace www.rumirestaurant.ch
```

## Contact Information

**For DNS Issues:**
- Infomaniak Support: https://www.infomaniak.com/support

**For AWS/Kubernetes Issues:**
- Contact DevOps team
- AWS Support (if applicable)

## Revision History

| Date | Change | Updated By |
|------|--------|------------|
| 2025-10-17 | Initial documentation created | Copilot |
| 2025-10-17 | Updated with new cluster config (z2h-eks) | Copilot |

---

**Last Updated:** October 17, 2025  
**Next Review:** When cluster changes occur

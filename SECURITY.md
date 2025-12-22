# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.x     | :white_check_mark: |

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability, please report it responsibly.

### How to Report

1. **Do NOT** create a public GitHub issue for security vulnerabilities
2. Use [GitHub Security Advisories](../../security/advisories/new) to report privately
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Any suggested fixes (optional)

### What to Expect

- **Initial Response**: Within 48 hours
- **Status Update**: Within 7 days
- **Resolution Timeline**: Depends on severity
  - Critical: 1-7 days
  - High: 7-14 days
  - Medium: 14-30 days
  - Low: 30-90 days

### Disclosure Policy

- We will work with you to understand and resolve the issue
- We will acknowledge your contribution in release notes (unless you prefer to remain anonymous)
- We ask that you give us reasonable time to address the issue before public disclosure

## Security Best Practices

When deploying HybridIdP, please follow these guidelines:

### Authentication & Secrets

- [ ] Use strong, unique passwords for all database connections
- [ ] Store certificates (PFX) securely with strong passwords
- [ ] Never commit `.env` files or certificates to version control
- [ ] Rotate secrets and certificates periodically

### Network Security

- [ ] Deploy behind a reverse proxy with SSL/TLS termination
- [ ] Use the `docker-compose.splithost-nginx-nodb.yml` for production
- [ ] Configure IP allowlisting in nginx
- [ ] Set up proper firewall rules (see `SPLIT_HOST_SECURITY.md`)

### Application Security

- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Configure `Proxy__KnownProxies` to only trust your reverse proxy
- [ ] Enable rate limiting
- [ ] Use MFA for admin accounts

### Monitoring

- [ ] Set up log aggregation (Loki/VictoriaLogs)
- [ ] Monitor `/health` endpoint
- [ ] Review audit logs regularly

## Known Security Considerations

### Development Configurations

The following files contain **development-only** placeholder values and should **never be used in production**:

- `appsettings.Development.json` - Contains sample connection strings
- `appsettings.Staging.json` - Contains sample connection strings

Production deployments should use environment variables or secure configuration providers.

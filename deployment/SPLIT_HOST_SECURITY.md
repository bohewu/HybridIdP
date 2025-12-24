# Split-Host Deployment Security Guide

> [!IMPORTANT]
> This is an **Advanced Hardening Guide**. Most users should first run the `setup-env` script and select a Split-Host mode. Use this guide to further harden the network layer (iptables) or understand the architecture.

This guide details the security architecture and hardening steps for a **Split-Host** deployment.

## Why Split-Host? (The DMZ Concept)

In high-security environments, we often separate the **Internet-Facing** components from the **Data & Application** components.

*   **Host A (DMZ / Public Zone)**:
    *   **Role**: Reverse Proxy (Nginx, HAProxy, etc.).
    *   **Exposure**: Open to the public internet (Ports 80/443).
    *   **Risk**: If compromised, the attacker only gains access to the proxy, not the database or application secrets.
*   **Host B (Trusted Zone / Intranet)**:
    *   **Role**: HybridIdP Application + Database.
    *   **Exposure**: **NO** public internet access. Only accepts traffic from Host A.
    *   **Benefit**: Your identity data (compliance) and keys are isolated from the public edge.

This guide ensures that Host B is strictly locked down to *only* talk to Host A.

## Architecture

```
┌──────────────────────────┐          ┌──────────────────────────────────────────┐
│  Host A (DMZ)            │          │  Host B (Trusted Zone)                   │
│  [ Public IP ]           │          │  [ Internal IP: 192.168.1.20 ]           │
│                          │ Traffic  │                                          │
│  ┌────────────────────┐  │ ───────► │  ┌─────────────────┐   ┌──────────────┐  │
│  │ Reverse Proxy      │  │ (8080)   │  │ Nginx Gateway   │──►│ HybridIdP    │  │
│  │ (SSL Termination)  │  │          │  │ (Docker)        │   │ (App)        │  │
│  └────────────────────┘  │          │  └─────────────────┘   └──────────────┘  │
└──────────────────────────┘          └──────────────────────────────────────────┘
```

---

## Deployment Options

| Option | Compose File | Security Level | Use Case |
|--------|--------------|----------------|----------|
| **Direct Expose** | `docker-compose.splithost.yml` | Medium | Simple setup, relies on iptables |
| **Local Nginx** | `docker-compose.splithost-nginx.yml` | High | App in internal network, nginx filters IPs |

> [!TIP]
> **Recommended**: Use `splithost-nginx.yml` for production. The app is completely isolated in Docker internal network.

---

## Prerequisites

- Both hosts on the same internal network
- Root/sudo access on Host B
- A working Nginx installation on Host A

---

## Step 1: Identify Network Interface (Host B)

First, identify the correct network interface for your iptables rules.

```bash
# List all network interfaces
ip addr show

# Example output:
# 1: lo: <LOOPBACK,UP,LOWER_UP> ...
#     inet 127.0.0.1/8 scope host lo
# 2: eth0: <BROADCAST,MULTICAST,UP,LOWER_UP> ...
#     inet 192.168.1.20/24 brd 192.168.1.255 scope global eth0
# 3: docker0: <NO-CARRIER,BROADCAST,MULTICAST,UP> ...
#     inet 172.17.0.1/16 brd 172.17.255.255 scope global docker0
```

**Key interfaces:**
- `eth0` (or `ens33`, `enp0s3`) = Physical network interface (use this for iptables)
- `docker0` = Docker bridge (don't use this)
- `lo` = Loopback (localhost only)

**Find your internal IP:**
```bash
# Get the IP address of your main interface
hostname -I | awk '{print $1}'
```

---

## Step 2: Configure iptables (Host B)

### 2.1 Verify Docker is Using the DOCKER-USER Chain

```bash
# Check existing rules
sudo iptables -L DOCKER-USER -n -v

# Expected output (if no custom rules yet):
# Chain DOCKER-USER (1 references)
# target     prot opt source               destination
# RETURN     all  --  0.0.0.0/0            0.0.0.0/0
```

### 2.2 Add Rule to Allow Only Host A (Reverse Proxy)

```bash
# Replace values:
# - eth0: Your network interface (from Step 1)
# - 192.168.1.10: Host A's IP address (your reverse proxy)
# - 8080: HybridIdP port

sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 8080 ! -s 192.168.1.10 -j DROP
```

**Explanation:**
- `-I DOCKER-USER`: Insert at the top of DOCKER-USER chain
- `-i eth0`: Only apply to traffic coming from eth0 interface
- `-p tcp --dport 8080`: Only for TCP port 8080
- `! -s 192.168.1.10`: NOT from source IP 192.168.1.10
- `-j DROP`: Drop the packet

### 2.3 Verify the Rule

```bash
sudo iptables -L DOCKER-USER -n -v

# Expected output:
# Chain DOCKER-USER (1 references)
# target     prot opt source               destination
# DROP       tcp  --  !192.168.1.10        0.0.0.0/0            tcp dpt:8080
# RETURN     all  --  0.0.0.0/0            0.0.0.0/0
```

### 2.4 Persist Rules After Reboot

**Ubuntu/Debian:**
```bash
sudo apt install iptables-persistent
sudo netfilter-persistent save
```

**CentOS/RHEL:**
```bash
sudo yum install iptables-services
sudo service iptables save
sudo systemctl enable iptables
```

### 2.5 (Optional) Allow Multiple Source IPs

If you have multiple reverse proxies:
```bash
# Allow 192.168.1.10 and 192.168.1.11
sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 8080 -s 192.168.1.10 -j ACCEPT
sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 8080 -s 192.168.1.11 -j ACCEPT
sudo iptables -I DOCKER-USER -i eth0 -p tcp --dport 8080 -j DROP
```

---

## Step 3: Configure Nginx (Host A)

### 3.1 Basic Reverse Proxy Configuration

Create `/etc/nginx/sites-available/hybrididp`:

```nginx
upstream hybrididp_backend {
    server 192.168.1.20:8080;  # Host B's IP and port
    keepalive 32;
}

server {
    listen 80;
    server_name idp.example.com;
    
    # Redirect HTTP to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name idp.example.com;

    # SSL Certificates
    ssl_certificate /etc/nginx/ssl/idp.example.com.crt;
    ssl_certificate_key /etc/nginx/ssl/idp.example.com.key;
    
    # Modern SSL Configuration
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
    ssl_prefer_server_ciphers off;

    # Proxy Headers (CRITICAL for HybridIdP)
    location / {
        proxy_pass http://hybrididp_backend;
        
        # Pass original client information
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;  # CRITICAL!
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port $server_port;
        
        # WebSocket support (for SignalR)
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
}
```

### 3.2 Enable the Site

```bash
# Create symbolic link
sudo ln -s /etc/nginx/sites-available/hybrididp /etc/nginx/sites-enabled/

# Test configuration
sudo nginx -t

# Reload Nginx
sudo systemctl reload nginx
```

### 3.3 Verify Headers Are Forwarded

Test that headers are being sent correctly:

```bash
# From Host A, test the connection
curl -I http://192.168.1.20:8080

# From a client, test the full flow
curl -I https://idp.example.com
```

---

## Step 4: Configure HybridIdP (Host B)

### 4.1 Create .env File

```bash
cd deployment
./setup-env.sh  # or .\setup-env.ps1 on Windows
```

Or manually create `.env`:

```bash
# Core settings
ASPNETCORE_ENVIRONMENT=Production
DATABASE_PROVIDER=SqlServer

# Trust the reverse proxy
Proxy__Enabled=true
PROXY_HOST_IP=192.168.1.10

# Database (adjust as needed)
ConnectionStrings__SqlServerConnection=Server=mssql-service;Database=HybridAuthIdP;...
```

### 4.2 Start the Application

```bash
docker compose -f docker-compose.internal.yml --env-file .env up -d
```

---

## Step 5: Verification

### 5.1 Test Connectivity

From Host A (Reverse Proxy):
```bash
# Should succeed
curl http://192.168.1.20:8080/health
```

From any other host:
```bash
# Should fail (connection refused or timeout)
curl http://192.168.1.20:8080/health
```

### 5.2 Test Full Flow

Open browser and navigate to:
```
https://idp.example.com
```

Check that:
- [ ] Page loads correctly
- [ ] URLs in the page use `https://` (not `http://`)
- [ ] Login works
- [ ] OAuth flows complete successfully

### 5.3 Verify Client IP Logging

Check HybridIdP logs to confirm real client IPs are being captured:

```bash
docker logs <container_id> | grep "Client IP"
```

---

## Troubleshooting

### URLs Show http:// Instead of https://

**Problem:** OAuth redirects and links use `http://` scheme.

**Cause:** `X-Forwarded-Proto` header not being passed or trusted.

**Solution:**
1. Verify Nginx is sending `X-Forwarded-Proto`:
   ```bash
   curl -v http://192.168.1.20:8080 -H "X-Forwarded-Proto: https"
   ```
2. Ensure `.env` has:
   ```
   Proxy__Enabled=true
   PROXY_HOST_IP=192.168.1.10
   ```

### Connection Refused from Host A

**Problem:** Reverse proxy can't connect to HybridIdP.

**Cause:** iptables rule blocking traffic.

**Solution:** Check iptables rules:
```bash
sudo iptables -L DOCKER-USER -n -v
```

Ensure Host A's IP is allowed.

### Docker Container Can't Start

**Problem:** Container fails to bind to port.

**Cause:** Port already in use or permission issue.

**Solution:**
```bash
# Check what's using port 8080
sudo lsof -i :8080
```

---

## Summary

With this setup:
- **Docker doesn't need special config** (uses standard compose file)
- **Security is at iptables level** (DOCKER-USER chain)
- **Nginx forwards all necessary headers** for proper scheme/IP detection
- **HybridIdP's ForwardedHeadersMiddleware** processes the headers

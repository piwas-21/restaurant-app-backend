# API Routing Logic Explained

The **Kubernetes Ingress** acts as a smart router that examines incoming HTTP requests and forwards them to the appropriate service based on the URL path.

---

## Complete Request Flow Diagram

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ 1. User's Browser                                                             │
│    https://rumirestaurant.ch/api/Categories                                   │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │
                           ↓
┌──────────────────────────────────────────────────────────────────────────────┐
│ 2. DNS (Infomaniak)                                                           │
│    rumirestaurant.ch → ALB IP addresses                                       │
│    (18.195.121.11, 3.122.88.28, 35.159.5.16)                                 │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │
                           ↓
┌──────────────────────────────────────────────────────────────────────────────┐
│ 3. AWS Application Load Balancer (ALB)                                        │
│    - Terminates SSL/TLS (uses ACM certificate)                                │
│    - Forwards to Kubernetes Ingress Controller                                │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │
                           ↓
┌──────────────────────────────────────────────────────────────────────────────┐
│ 4. Kubernetes Ingress (ingress.yaml)                                          │
│    Examines request path and routes accordingly:                              │
│                                                                                │
│    IF path starts with /api                                                   │
│    THEN → rumi-restaurant-backend:80                                          │
│    ELSE → rumi-restaurant-web:80                                              │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │
                           ├─────────────────────────────────────────────────────┐
                           │                                                     │
                           ↓ /api                                                ↓ /
┌───────────────────────────────────────────┐  ┌──────────────────────────────────────┐
│ 5a. Backend Service                        │  │ 5b. Frontend Service                 │
│     rumi-restaurant-backend:80             │  │     rumi-restaurant-web:80           │
│     (ClusterIP - internal only)            │  │     (ClusterIP - internal only)      │
└───────────────────┬───────────────────────┘  └──────────────┬───────────────────────┘
                    │                                           │
                    ↓                                           ↓
┌───────────────────────────────────────────┐  ┌──────────────────────────────────────┐
│ 6a. Backend Pod (port 8080)                │  │ 6b. Frontend Pod (port 3000)         │
│     .NET API running on 8080               │  │     Next.js server on 3000           │
│     Handles: /api/Categories,              │  │     Serves: HTML, React, static      │
│              /api/Auth/login, etc.         │  │             assets                   │
└───────────────────────────────────────────┘  └──────────────────────────────────────┘
```

---

## Detailed Step-by-Step Flow

### Step 1: Frontend Makes API Call

**File:** `src/services/categoryService.ts`
```typescript
const CATEGORIES_API_URL = '/api/Categories';  // ← Relative path!

export const getCategories = async () => {
  const response = await apiClient.get(CATEGORIES_API_URL);
  return response.json();
};
```

**File:** `src/services/apiClient.ts`
```typescript
// API_BASE_URL is baked in at build time
const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5221';
// Production: API_BASE_URL = 'https://rumirestaurant.ch'

const apiClient = {
  get: (endpoint: string) => {
    // For relative paths like '/api/Categories'
    const url = endpoint.startsWith('http')
      ? endpoint
      : `${API_BASE_URL}${endpoint}`;

    // Result: https://rumirestaurant.ch/api/Categories
    return fetchWithAuth(url);
  }
};
```

**Note:** Some services use absolute URLs:
```typescript
// authService.ts uses absolute URL
const AUTH_API_URL = `${API_BASE_URL}/api/Auth`;  // Already includes /api

// Other services use relative paths
const CATEGORIES_API_URL = '/api/Categories';      // apiClient adds base URL
```

### Step 2: Request Reaches AWS ALB

The DNS resolves `rumirestaurant.ch` to the ALB's IP addresses. The ALB:
- Terminates SSL/TLS using ACM certificate
- Forwards the request to the Kubernetes cluster
- Path remains: `/api/Categories`

### Step 3: Kubernetes Ingress Routes the Request

**File:** `rumi-argocd-gitops/base/ingress.yaml`
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: rumi-restaurant-web
  namespace: rumi
spec:
  ingressClassName: alb
  rules:
  - host: rumirestaurant.ch
    http:
      paths:
      # ⭐ PATH-BASED ROUTING - This is the magic!

      # Rule 1: API requests → Backend
      - path: /api
        pathType: Prefix           # ← Matches any path starting with /api
        backend:
          service:
            name: rumi-restaurant-backend   # ← Routes to backend service
            port:
              number: 80

      # Rule 2: Everything else → Frontend
      - path: /
        pathType: Prefix           # ← Matches everything
        backend:
          service:
            name: rumi-restaurant-web       # ← Routes to frontend service
            port:
              number: 80
```

**How Ingress Path Matching Works:**

| Request Path | Matches Rule | Routes To | Why |
|-------------|-------------|-----------|-----|
| `/api/Categories` | `/api` (Rule 1) | Backend | Starts with `/api` |
| `/api/Auth/login` | `/api` (Rule 1) | Backend | Starts with `/api` |
| `/api/health` | `/api` (Rule 1) | Backend | Starts with `/api` |
| `/` | `/` (Rule 2) | Frontend | Root path |
| `/menu` | `/` (Rule 2) | Frontend | Doesn't start with `/api` |
| `/auth/login` | `/` (Rule 2) | Frontend | Doesn't start with `/api` |

**Important:** Rules are evaluated in order. More specific rules (like `/api`) should come before general rules (like `/`).

### Step 4: Backend Service Receives Request

**File:** `rumi-argocd-gitops/base/backend-deployment.yaml`
```yaml
apiVersion: v1
kind: Service
metadata:
  name: rumi-restaurant-backend
  namespace: rumi
spec:
  selector:
    app: rumi-restaurant-backend    # ← Targets backend pods
  ports:
  - port: 80                        # ← Service listens on port 80
    targetPort: 8080                # ← Forwards to container port 8080
    protocol: TCP
  type: ClusterIP                   # ← Internal only (not exposed externally)
```

**Port Mapping:**
```
Ingress (80) → Service (80) → Pod Container (8080)
```

### Step 5: Backend Pod Handles Request

**Backend Container:**
- Runs .NET Web API on port 8080
- Has endpoints like:
  - `GET /api/Categories`
  - `POST /api/Auth/login`
  - `GET /api/Products`
  - `GET /api/health`

**File:** `backend/RestaurantSystem.Api/Program.cs` (example)
```csharp
// The backend expects full path with /api prefix
app.MapControllers();  // Routes: /api/Categories, /api/Auth, etc.
```

---

## Key Architecture Concepts

### 1. Path-Based Routing

The Ingress uses **path prefixes** to determine which service should handle the request:

```yaml
# Ingress configuration
/api/*  → Backend Service   # API calls
/*      → Frontend Service  # Web pages, static files
```

This is similar to nginx configuration:
```nginx
# Equivalent nginx config
location /api {
    proxy_pass http://backend:8080;
}

location / {
    proxy_pass http://frontend:3000;
}
```

### 2. Service Discovery

Kubernetes Services provide **internal DNS names**:
- `rumi-restaurant-backend.rumi.svc.cluster.local` → Backend pods
- `rumi-restaurant-web.rumi.svc.cluster.local` → Frontend pods

The Ingress uses short names (`rumi-restaurant-backend`) because they're in the same namespace.

### 3. Port Mapping

```
┌─────────────────────────────────────────────────────────┐
│ External Request                                         │
│ https://rumirestaurant.ch:443/api/Categories            │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ↓
┌─────────────────────────────────────────────────────────┐
│ ALB (Port 443 → 80)                                      │
│ - SSL termination happens here                           │
│ - Forwards HTTP (not HTTPS) to Ingress                   │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ↓
┌─────────────────────────────────────────────────────────┐
│ Ingress Controller (Port 80)                             │
│ - Examines path                                          │
│ - Routes to appropriate service                          │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ↓
┌─────────────────────────────────────────────────────────┐
│ Backend Service (Port 80)                                │
│ - Internal Kubernetes service                            │
│ - Load balances across backend pods                      │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ↓
┌─────────────────────────────────────────────────────────┐
│ Backend Pod (Port 8080)                                  │
│ - .NET application listening on 8080                     │
│ - Handles the actual API request                         │
└─────────────────────────────────────────────────────────┘
```

### 4. Why Frontend Uses Same Domain

**Problem Solved:** Cross-Origin Resource Sharing (CORS)

If frontend and backend were on different domains:
```
Frontend: https://app.rumirestaurant.ch
Backend:  https://api.rumirestaurant.ch  ← Different subdomain!
```

You'd need:
- ❌ Complex CORS configuration
- ❌ Preflight OPTIONS requests (slower)
- ❌ Cookie/credential handling issues

**Current Solution:** Same domain, path-based routing:
```
Frontend: https://rumirestaurant.ch/
Backend:  https://rumirestaurant.ch/api
```

Benefits:
- ✅ No CORS issues (same origin)
- ✅ Simpler authentication (cookies work automatically)
- ✅ Better performance (no preflight requests)
- ✅ Single SSL certificate

---

## Example Request Flow

### Example 1: Loading Menu Page

```
1. User visits: https://rumirestaurant.ch/menu
   ↓
2. DNS resolves to ALB IP
   ↓
3. ALB terminates SSL, forwards to Ingress
   ↓
4. Ingress checks path: /menu
   - Does NOT start with /api
   - Matches rule: path: /
   - Routes to: rumi-restaurant-web:80
   ↓
5. Frontend Service forwards to a frontend pod on port 3000
   ↓
6. Next.js server returns HTML page
   ↓
7. Browser receives menu page and renders it
```

### Example 2: Fetching Categories (API Call)

```
1. Frontend JavaScript calls: apiClient.get('/api/Categories')
   - Becomes: https://rumirestaurant.ch/api/Categories
   ↓
2. Browser sends request to: https://rumirestaurant.ch/api/Categories
   ↓
3. DNS resolves to ALB IP
   ↓
4. ALB terminates SSL, forwards to Ingress
   ↓
5. Ingress checks path: /api/Categories
   - Starts with /api ✓
   - Matches rule: path: /api
   - Routes to: rumi-restaurant-backend:80
   ↓
6. Backend Service forwards to a backend pod on port 8080
   ↓
7. .NET API CategoriesController handles GET /api/Categories
   ↓
8. Returns JSON: { "data": [...categories...] }
   ↓
9. Response flows back through Ingress → ALB → Browser
   ↓
10. Frontend receives categories and displays them
```

---

## Configuration Files Summary

### 1. Frontend Environment (Build Time)
**File:** `.env.production` or GitLab CI build args
```bash
NEXT_PUBLIC_API_URL=https://rumirestaurant.ch
```

**Used in:** `src/services/apiClient.ts`
```typescript
const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL;
// Result: All API calls go to https://rumirestaurant.ch/api/*
```

### 2. Kubernetes Ingress (Routing Rules)
**File:** `rumi-argocd-gitops/base/ingress.yaml`
```yaml
# Path-based routing rules
- path: /api       → backend
- path: /          → frontend
```

### 3. Backend Service (Internal Networking)
**File:** `rumi-argocd-gitops/base/backend-deployment.yaml`
```yaml
Service:
  name: rumi-restaurant-backend
  port: 80 → targetPort: 8080
```

### 4. Frontend Service (Internal Networking)
**File:** `rumi-argocd-gitops/base/frontend-deployment.yaml`
```yaml
Service:
  name: rumi-restaurant-web
  port: 80 → targetPort: 3000
```

---

## Common Questions

### Q: Why does frontend use `https://rumirestaurant.ch` instead of backend service name?

**A:** Because the frontend code runs **in the user's browser**, not in the Kubernetes cluster. The browser doesn't have access to internal Kubernetes DNS names like `rumi-restaurant-backend.rumi.svc.cluster.local`.

```javascript
// ❌ This won't work (browser can't resolve K8s DNS)
const API_BASE_URL = 'http://rumi-restaurant-backend.rumi.svc.cluster.local';

// ✅ This works (browser can access public domain)
const API_BASE_URL = 'https://rumirestaurant.ch';
```

### Q: Why not use different subdomains for API?

**A:** Path-based routing (`/api`) is simpler than subdomain-based routing:

| Approach | Pros | Cons |
|----------|------|------|
| **Path-based** (`/api`) | No CORS issues, single certificate, simpler config | All traffic through one ingress |
| **Subdomain** (`api.example.com`) | Better separation, easier caching rules | CORS complexity, multiple certificates |

For a monolithic application like Rumi Restaurant, path-based routing is more appropriate.

### Q: What if I want to call backend from frontend server-side code?

**A:** In Next.js server-side code (API routes, getServerSideProps), you could theoretically use internal Kubernetes service names:

```typescript
// In Next.js API route or getServerSideProps (runs on server)
const internalApiUrl = process.env.INTERNAL_API_URL || 'http://rumi-restaurant-backend.rumi.svc.cluster.local';

// But it's simpler to just use the same public URL
const apiUrl = 'https://rumirestaurant.ch/api';
```

---

## Troubleshooting

### Issue: API calls go to frontend instead of backend

**Symptoms:**
- Frontend returns HTML instead of JSON
- 404 errors for API endpoints

**Cause:** Ingress path rules are out of order

**Fix:** Ensure `/api` rule comes before `/` rule:
```yaml
paths:
  - path: /api      # ← More specific rule first
    ...
  - path: /         # ← Generic rule last
    ...
```

### Issue: Backend receives requests without `/api` prefix

**Cause:** Path rewriting is enabled (it shouldn't be)

**Fix:** Remove any `nginx.ingress.kubernetes.io/rewrite-target` annotations:
```yaml
# ❌ Don't use this
annotations:
  nginx.ingress.kubernetes.io/rewrite-target: /

# ✅ No rewrite - backend gets full path including /api
```

### Issue: CORS errors even with same domain

**Cause:** Mixed HTTP/HTTPS or different ports

**Fix:** Ensure consistent protocol:
```typescript
// ❌ Mixed protocols cause CORS
const API_BASE_URL = 'http://rumirestaurant.ch';  // HTTP
// Page loaded with https://rumirestaurant.ch      // HTTPS

// ✅ Same protocol
const API_BASE_URL = 'https://rumirestaurant.ch';
```

---

## Summary

**The magic happens in the Kubernetes Ingress:**

1. **Frontend code** makes requests to `https://rumirestaurant.ch/api/*`
2. **Ingress examines the path** and sees it starts with `/api`
3. **Ingress routes** the request to `rumi-restaurant-backend` service
4. **Backend service** forwards to backend pods on port 8080
5. **Backend API** handles the request and returns response

**The key insight:** The Ingress acts as a **reverse proxy** that routes traffic based on URL paths, allowing frontend and backend to share the same domain while running as separate services.

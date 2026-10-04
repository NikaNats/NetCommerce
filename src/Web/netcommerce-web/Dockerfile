# NetCommerce storefront — production image (Next.js standalone).
#
# Build (from the repo root, so the tag names the service):
#   docker build \
#     -f src/Web/netcommerce-web/Dockerfile \
#     --build-arg NEXT_PUBLIC_API_ORIGIN=https://api.example.com \
#     --build-arg PUBLIC_API_ORIGIN=https://api.example.com \
#     --build-arg STORAGE_ORIGIN=https://cdn.example.com \
#     -t netcommerce-web:prod src/Web/netcommerce-web
#
# Why the build args: NEXT_PUBLIC_* values are INLINED into the browser bundle
# at build time (not read at runtime), and the CSP allowlists in next.config.ts
# are baked the same way. A build without them serves a CSP and a hub URL that
# do not match the deployment — the failure mode is silent (blocked images, a
# dead order-tracking socket), so pass the production values explicitly.
#
# Runtime env (all required in production — readConfig throws naming the
# missing one rather than booting onto localhost defaults):
#   API_BASE_URL, KEYCLOAK_BASE_URL, KEYCLOAK_REALM, KEYCLOAK_CLIENT_ID,
#   PUBLIC_ORIGIN, REDIS_URL (+ the STORAGE_ORIGIN / *_API_ORIGIN pair above)

# ==============================================================================
# 1. Dependencies — cached until package-lock.json changes
# ==============================================================================
FROM node:20-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

# ==============================================================================
# 2. Build — needs the *production* origins (see header)
# ==============================================================================
FROM node:20-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

ARG NEXT_PUBLIC_API_ORIGIN=""
ARG PUBLIC_API_ORIGIN=""
ARG STORAGE_ORIGIN=""

# Dummy server origins carry the build past config validation for the pages
# that evaluate it; real values come from runtime env. These three MUST be real
# (they are baked into the client bundle / CSP), the rest MUST NOT (runtime).
ENV NEXT_PUBLIC_API_ORIGIN=${NEXT_PUBLIC_API_ORIGIN} \
    PUBLIC_API_ORIGIN=${PUBLIC_API_ORIGIN} \
    STORAGE_ORIGIN=${STORAGE_ORIGIN} \
    API_BASE_URL=http://localhost:5000 \
    KEYCLOAK_BASE_URL=http://localhost:8080 \
    KEYCLOAK_REALM=netcommerce \
    KEYCLOAK_CLIENT_ID=netcommerce-web \
    PUBLIC_ORIGIN=http://localhost:3000 \
    NEXT_TELEMETRY_DISABLED=1

RUN npm run build

# ==============================================================================
# 3. Runtime — standalone server plus its static assets, nothing else
# ==============================================================================
FROM node:20-alpine AS runner
WORKDIR /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME="0.0.0.0"

# next/standalone emits server.js + a minimal node_modules; static assets and
# the public dir ride alongside. Running as the image's `node` user (non-root).
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static

USER node
EXPOSE 3000

# Liveness probe: dependency-free by design (see api/health/live/route.ts), so
# a failing API or Redis never restarts a healthy web process. Readiness is the
# orchestrator's /api/health/ready check, which DOES gate on dependencies.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD node -e "fetch('http://127.0.0.1:3000/api/health/live').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"

CMD ["node", "server.js"]

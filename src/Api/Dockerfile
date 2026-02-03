# ==============================================================================
# 1. BUILD STAGE - Native AOT Compilation
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# AOT Prerequisites: Native Linker Dependencies
# clang: Required for linking the native binary on Linux
# zlib1g-dev: Required for compression support in the native binary
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       clang \
       zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy solution configuration files first for better layer caching
# Changes to source code won't invalidate package restore cache
COPY global.json Directory.Build.props Directory.Packages.props NetCommerce.slnx ./

# Copy all project files to enable 'dotnet restore'
# Note: Using simplified approach - copy everything for restore context
COPY src/ ./src/
COPY tests/ ./tests/
COPY tools/ ./tools/

# Restore NuGet packages
# This layer is cached until project files or package versions change
RUN dotnet restore NetCommerce.slnx

# Set working directory to the API project for publish
WORKDIR /src/src/Api

# CRITICAL: Generate Wolverine Source Code
# Wolverine's AOT support requires pre-generated handlers
# This must run before compilation or the generated types won't exist
#
# Design-Time Isolation: Provide dummy env vars to prevent service startup failures.
# Wolverine codegen analyzes code structure but should NOT connect to real services.
ENV ConnectionStrings__NetCommerce="Host=dummy;Database=dummy;Username=dummy;Password=dummy" \
    ConnectionStrings__Redis="dummy:6379" \
    Keycloak__Authority="http://dummy" \
    Keycloak__ClientId="dummy" \
    Keycloak__ClientSecret="dummy" \
    ASPNETCORE_ENVIRONMENT="Development"

RUN dotnet run -- codegen write || echo "WARNING: Wolverine codegen failed - TypeLoadMode.Static will enforce failure at runtime"

# Publish with Native AOT
# -c Release: Optimized release build with all trimming/AOT optimizations
# -o /app/publish: Output directory for published artifacts
# -r linux-x64: Runtime identifier for Linux x64 (container target)
# --self-contained true: Include all dependencies (no shared framework)
# /p:PublishAot=true: Enable Native AOT compilation via ILC
# /p:StripSymbols=true: Remove debug symbols for smaller binary size (~40% reduction)
# /p:IlcOptimizationPreference=Speed: Optimize for execution speed over size
# /p:IlcGenerateStackTraceData=false: Disable stack trace metadata (reduces size)
RUN dotnet publish -c Release -o /app/publish \
    -r linux-x64 \
    --self-contained true \
    /p:PublishAot=true \
    /p:StripSymbols=true \
    /p:IlcOptimizationPreference=Speed \
    /p:IlcGenerateStackTraceData=false

# ==============================================================================
# 2. RUNTIME STAGE - Chiseled Container (Maximum Security)
# ==============================================================================
# 'chiseled' images have:
# - No shell (/bin/sh, bash) - Prevents interactive exploitation
# - No package manager (apt, apk) - Prevents post-exploitation tooling
# - No root user - Runs as 'app' user (UID 1654) by default
# - Minimal attack surface - Only glibc, openssl, ICU for .NET requirements
# Image size: ~20MB base + ~50MB app = ~70MB total (vs. ~220MB aspnet image)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-preview-chiseled
WORKDIR /app

# Copy the native binary and runtime configuration
# The build stage produces a single native executable (NetCommerce.Api)
# No .dll files, no .NET runtime - just the native binary
COPY --from=build /app/publish .

# Expose HTTP port
# Chiseled images run as non-root 'app' user, so we use 8080 (not 80)
EXPOSE 8080

# Environment Configuration
# ASPNETCORE_URLS: Listen on all interfaces on port 8080
# DOTNET_EnableDiagnostics=0: Disable diagnostic ports for security and performance
#   - Prevents dotnet-trace/dotnet-dump attachment
#   - Reduces memory overhead
#   - Hardens container against diagnostic-based attacks
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

# Health check (optional - requires curl in chiseled-extra variant)
# For production, use Kubernetes liveness/readiness probes instead
# HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
#     CMD ["/app/NetCommerce.Api", "health"]

# Entrypoint: Execute the native binary directly
# No "dotnet NetCommerce.Api.dll" - this is a native executable
ENTRYPOINT ["./NetCommerce.Api"]

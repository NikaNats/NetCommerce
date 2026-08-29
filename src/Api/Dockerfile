# ==============================================================================
# 1. BUILD STAGE - Native AOT Compilation
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
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
COPY src/ ./src/
COPY tests/ ./tests/
COPY tools/ ./tools/

# Restore NuGet packages
# This layer is cached until project files or package versions change
RUN dotnet restore NetCommerce.slnx

# Set working directory to the API project for codegen and publish
WORKDIR /src/src/Api

# CRITICAL: Design-Time Isolation for Wolverine Source Code Generation
# Wolverine's AOT support requires pre-generated handlers (TypeLoadMode.Static).
# These dummy env vars prevent service startup/connection failures during 'codegen write'.
ENV ConnectionStrings__OrderingDb="Host=dummy;Database=dummy;Username=dummy;Password=dummy" \
    ConnectionStrings__postgres="Host=dummy;Database=dummy;Username=dummy;Password=dummy" \
    ConnectionStrings__NetCommerce="Host=dummy;Database=dummy;Username=dummy;Password=dummy" \
    ConnectionStrings__Redis="dummy:6379" \
    Keycloak__Authority="http://dummy" \
    Keycloak__ClientId="dummy" \
    Keycloak__ClientSecret="dummy" \
    ASPNETCORE_ENVIRONMENT="Development"

# Generate Wolverine static C# handler code into Internal/Generated/
# In Debug/Dev mode, WolverineFx.RuntimeCompilation is available to execute this step.
RUN dotnet run -- codegen write || echo "WARNING: Wolverine codegen failed - TypeLoadMode.Static will enforce failure at runtime"

# Publish with Native AOT (Release Mode)
# -c Release: Excludes WolverineFx.RuntimeCompilation (Roslyn) and optimizes binary size
# -r linux-x64: Target Linux x64 native binary
# /p:PublishAot=true: Ahead-of-Time compilation via ILC
# /p:StripSymbols=true: Strip symbols for ~40% smaller binary size
# /p:IlcOptimizationPreference=Speed: Maximize runtime execution speed
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
# - Non-root user - Runs as 'app' user (UID 1654) by default
# - Minimal attack surface - Minimal dependencies (~70MB total container size)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra
WORKDIR /app

# Copy the compiled native executable and config
COPY --from=build /app/publish .

# Expose non-root HTTP port
EXPOSE 8080

# Environment Hardening
# ASPNETCORE_URLS: Listen on port 8080
# DOTNET_EnableDiagnostics=0: Disable diagnostic ports for performance & security
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

# Entrypoint: Execute the native binary directly
ENTRYPOINT ["./NetCommerce.Api"]

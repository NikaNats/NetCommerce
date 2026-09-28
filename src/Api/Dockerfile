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
# NOTE: .editorconfig and .globalconfig MUST be copied: analyzer severities
# (including per-folder suppressions for scaffolded migrations) must match
# local builds exactly, otherwise Docker-only analyzer errors appear.
COPY global.json Directory.Build.props Directory.Packages.props NetCommerce.slnx .editorconfig .globalconfig ./

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
# HARD FAIL on codegen errors: TypeLoadMode.Static means any handler missing from the
# generated output silently does not exist in the AOT production binary. A fallback
# (e.g. `|| echo`) here would ship a "successfully built" image with dropped handlers.
RUN dotnet run -- codegen write

# Publish as self-contained JIT (Release Mode)
# -c Release: Excludes WolverineFx.RuntimeCompilation (Roslyn) and optimizes binary size
# -r linux-x64: Target Linux x64 native binary
# --self-contained true: Ships CoreCLR so the image runs on runtime-deps
# /p:PublishAot=false: NativeAOT is DISABLED ON PURPOSE. EF Core cannot build
# models under PublishAot switches (no compiled models exist yet), which breaks
# every data path at runtime. All release verification (600+ tests) runs JIT;
# re-enable AOT only after EF compiled models land for all bounded contexts.
# /p:StripSymbols=true: Strip symbols for ~40% smaller binary size
RUN dotnet publish -c Release -o /app/publish \
    -r linux-x64 \
    --self-contained true \
    /p:PublishAot=false \
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
# NOTE: the '-extra' variant is REQUIRED (not plain 'chiseled'): it carries the
# ICU libraries the app needs for non-English formatting (GEL/Georgian Lari;
# see InvariantGlobalization=false in NetCommerce.Api.csproj). Verified: -extra
# still ships zero shells - only ICU/data files on top of plain chiseled.
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

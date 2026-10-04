#!/usr/bin/env bash
# Run EXACTLY what pr-validation.yml runs, with the env it now declares.
#
# This exists to prove the fix before it reaches CI. The three Payments tests that
# failed with "Missing connection string: ShippingDb" should now pass against real
# Postgres and Redis — turning "I believe this works" into "this works".
set -uo pipefail
cd "$(dirname "$0")/.."

export CONNECTIONSTRINGS__CATALOGDB="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=catalog"
export CONNECTIONSTRINGS__ORDERINGDB="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=ordering"
export CONNECTIONSTRINGS__INVENTORYDB="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=inventory"
export CONNECTIONSTRINGS__PAYMENTSDB="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=payments"
export CONNECTIONSTRINGS__FINANCEDB="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=finance"
export CONNECTIONSTRINGS__SHIPPINGDB="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=shipping"
export CONNECTIONSTRINGS__POSTGRES="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!"
export CONNECTIONSTRINGS__REDIS="127.0.0.1:6379"
export Stripe__SecretKey="sk_test_mock"
export Stripe__WebhookSecret="whsec_test_secret"
export Stripe__AllowTestModeInProduction="true"

dotnet test --nologo -c Release \
  --filter "Category!=Integration&Category!=AutomatedLoadTest"
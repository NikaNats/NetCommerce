#!/usr/bin/env bash
# Shared connection-string env for local integration runs.
# Mirrors pr-validation.yml exactly — if these drift, the local reproduction
# stops predicting CI.
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
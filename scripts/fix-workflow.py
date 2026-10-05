import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Remove annotations field
if 'annotations' in data:
    del data['annotations']

# Add cards
data['cards'] = [
    {
      'dot': 'emerald',
      'title': 'Three CI Workflows',
      'items': [
        'pr-validation.yml — PR gate (PostgreSQL 17 + Redis 8 services)',
        'native-aot-verification.yml — AOT trim boundaries + container smoke',
        'release.yml — Tag-only, zero bypass flags, production vars required'
      ]
    },
    {
      'dot': 'amber',
      'title': 'Zero-Bypass Release',
      'items': [
        'release.yml NEVER sets AllowTestModeInProduction',
        'PROD_API_ORIGIN, PROD_STORAGE_ORIGIN, PROD_PUBLIC_ORIGIN required',
        'Images carry no runtime secrets; injected at deploy from secret manager'
      ]
    },
    {
      'dot': 'rose',
      'title': 'Saga Compensation (3 Timeouts)',
      'items': [
        'Inventory Reservation: 5m → cancel order, release stock (State.cs:196)',
        'Payment Confirmation: 5m → cancel payment, refund (State.cs:226)',
        'Shipping Stall: 30m → escalate, contact support (State.cs:256)',
        'No silent failures — every timeout has explicit compensation'
      ]
    }
  ]

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
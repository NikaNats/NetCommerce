import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Adjust saga lane columns to avoid overlaps
for node in data['nodes']:
    if node['id'] == 'reserve':
        node['col'] = 1
    elif node['id'] == 'reserved':
        node['col'] = 1
    elif node['id'] == 'request-pay':
        node['col'] = 2
    elif node['id'] == 'pay-succeed':
        node['col'] = 3
    elif node['id'] == 'fulfilled':
        node['col'] = 4

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Adjust saga lane columns properly
for node in data['nodes']:
    if node['id'] == 'saga-start':
        node['col'] = 0
    elif node['id'] == 'reserve':
        node['col'] = 1
    elif node['id'] == 'reserved':
        node['col'] = 2
    elif node['id'] == 'request-pay':
        node['col'] = 3
    elif node['id'] == 'pay-succeed':
        node['col'] = 4
    elif node['id'] == 'fulfilled':
        node['col'] = 5

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
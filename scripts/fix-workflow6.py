import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Adjust saga lane columns - move reserve to col 0, reserved to col 1, request-pay to col 2
for node in data['nodes']:
    if node['id'] == 'reserve':
        node['col'] = 0
    elif node['id'] == 'reserved':
        node['col'] = 1
    elif node['id'] == 'request-pay':
        node['col'] = 2

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
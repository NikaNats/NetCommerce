import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Adjust saga lane columns - move reserved to col 2
for node in data['nodes']:
    if node['id'] == 'reserved':
        node['col'] = 2

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Move dotnet-restore to col 0 and build-verify to col 2 to avoid overlap
for node in data['nodes']:
    if node['id'] == 'dotnet-restore':
        node['col'] = 0
    elif node['id'] == 'build-verify':
        node['col'] = 2
    elif node['id'] == 'arch-tests':
        node['col'] = 2

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
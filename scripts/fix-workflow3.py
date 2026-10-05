import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

# Move aot-trim to col 0 and compile-aot to col 2 to avoid overlap
for node in data['nodes']:
    if node['id'] == 'aot-trim':
        node['col'] = 0
    elif node['id'] == 'compile-aot':
        node['col'] = 2
    elif node['id'] == 'container-smoke':
        node['col'] = 3

with open('docs/diagrams/workflow.json', 'w') as f:
    json.dump(data, f, indent=2)
print('Done')
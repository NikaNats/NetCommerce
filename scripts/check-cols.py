import json
with open('docs/diagrams/workflow.json', 'r') as f:
    data = json.load(f)

for node in data['nodes']:
    if node['lane'] == 'saga':
        print(node['id'], node.get('col'))
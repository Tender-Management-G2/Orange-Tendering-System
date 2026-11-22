"""Self-contained kit check: run `python 08_TOOLS/verify_kit.py` from extracted kit."""
from pathlib import Path
import json
root=Path(__file__).resolve().parents[1]
required=[
 'MASTER_PROMPT.md',
 '01_REQUIREMENTS/MVC Project - Updates (original).pdf',
 '01_REQUIREMENTS/REQUIREMENTS_MATRIX.md',
 '02_PRODUCT_BLUEPRINT/00_NORTH_STAR.md',
 '03_VISUAL_REFERENCES/01_Suppliers_Primary_Cinematic_Concept.png',
 '03_VISUAL_REFERENCES/03_Tenders_DeepDive_OneTab.png',
 '03_VISUAL_REFERENCES/04_Forensics_Evidence_OneTab.png',
 '03_VISUAL_REFERENCES/05_AI_Studio_Generative_OneTab.png',
 '05_DESIGN_SYSTEM/tokens.css',
 '07_CLAUDE_SKILLS/.claude/skills/03-oi-supplier-universe/SKILL.md']
missing=[x for x in required if not (root/x).is_file()]
skills=list((root/'07_CLAUDE_SKILLS/.claude/skills').glob('*/SKILL.md'))
images=list((root/'03_VISUAL_REFERENCES').glob('*.png'))
print(json.dumps({'root':str(root),'missing':missing,'skill_count':len(skills),'images_count':len(images),'files_count':sum(1 for p in root.rglob('*') if p.is_file())},indent=2))
if missing or len(skills)!=8 or len(images)<7: raise SystemExit('Kit incomplete')
print('KIT CHECK: OK')

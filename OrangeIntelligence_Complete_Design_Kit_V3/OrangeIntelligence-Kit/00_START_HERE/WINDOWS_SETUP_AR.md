# خطوات التشغيل في Windows PowerShell

```powershell
# عدّل المسار ليتطابق مع مكان ZIP الذي حملته:
$repo = "$HOME\Desktop\Orange\Projects\Orange-Tendering\Orange-Tendering-System-FinalVersion"
Set-Location $repo
# إذا كانت الحزمة في مجلد الـ repo وفككت باسم OrangeIntelligence-Kit:
Test-Path ".\OrangeIntelligence-Kit\MASTER_PROMPT.md"
# للتدقيق المحلي دون أي تعديل:
powershell -ExecutionPolicy Bypass -File ".\OrangeIntelligence-Kit\08_TOOLS\Preflight.ps1" -RepoPath "$repo"
# افحص skills المقترحة دون نسخ:
powershell -ExecutionPolicy Bypass -File ".\OrangeIntelligence-Kit\08_TOOLS\Install-Skills.ps1" -RepoPath "$repo"
# عند الرغبة فقط في نسخ المهارات غير الموجودة:
powershell -ExecutionPolicy Bypass -File ".\OrangeIntelligence-Kit\08_TOOLS\Install-Skills.ps1" -RepoPath "$repo" -Apply
```

الـ ZIP يحتوي مجلدًا رئيسيًا `OrangeIntelligence-Kit`؛ افك الضغط في جذر المستودع (لا داخل `Orange-Tendering-System/` حيث ملف csproj). من Claude Code افتح `OrangeIntelligence-Kit/MASTER_PROMPT.md` بقراءة ملف مباشرة بدل توقع ظهوره في قائمة `@`. لا ترسل رموز الوصول أو passwords أو ملفات `.env` داخل المحادثة.

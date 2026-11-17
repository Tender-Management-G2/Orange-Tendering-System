using Orange_Tendering_System.Services;

public static class ExecutiveNavigationChecks
{
    public static void Run()
    {
        ExecutivePage[] officer = [
            new("SourcingOfficer:Workflow:Index", "Tender", "SourcingOfficer", "/SourcingOfficer/Workflow", ["العطاءات", "المناقصات", "tenders"]),
            new("SourcingOfficer:Reports:Index", "Reports", "SourcingOfficer", "/SourcingOfficer/Reports", ["التقارير", "reports"]),
            new("SourcingOfficer:Dashboard:Index", "Dashboard", "SourcingOfficer", "/SourcingOfficer/Dashboard", ["لوحة التحكم", "الرئيسية", "dashboard"])
        ];
        var count=0;
        void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);Console.WriteLine("PASS "+ ++count +" "+label);}
        foreach(var phrase in new[]{"افتحلي صفحة التقارير","افتح لي التقارير","اعرض لي صفحة التَّقارير","Please open the reports page","go to reports"})
            Check(ExecutiveNavigationService.Match(phrase,"SourcingOfficer",officer).Url=="/SourcingOfficer/Reports","exact bilingual reports destination: "+phrase);
        foreach(var phrase in new[]{"افتحلي صفحة المناقصات","روح على العطاءات","open tenders"})
            Check(ExecutiveNavigationService.Match(phrase,"SourcingOfficer",officer).Url=="/SourcingOfficer/Workflow","exact tender destination: "+phrase);
        foreach(var phrase in new[]{"افتح لوحة التحكم","open dashboard"})
            Check(ExecutiveNavigationService.Match(phrase,"SourcingOfficer",officer).Url=="/SourcingOfficer/Dashboard","exact dashboard destination: "+phrase);
        foreach(var phrase in new[]{"افتح المستخدمين","open https://evil.example","افتحلي تقرير المناقصة 7","احذف العطاء","اعتمد التقرير","open reports and cancel tender"})
            Check(ExecutiveNavigationService.Match(phrase,"SourcingOfficer",officer).State=="Unrecognized","does not guess or execute a mutation: "+phrase);
        var both=officer.Concat([new ExecutivePage("Admin:Dashboard:Index","Dashboard","Admin","/Admin/Dashboard",["لوحة التحكم"])]).ToArray();
        Check(ExecutiveNavigationService.Match("افتح لوحة التحكم",null,both).State=="Choose","multiple workspaces ask which page");
        Check(ExecutiveNavigationService.Match("افتح لوحة التحكم","Admin",both).Url=="/Admin/Dashboard","current authorized workspace disambiguates");
        Check(ExecutiveNavigationService.Match("open reports",null,[]).Url==null,"no destination is created outside supplied permitted catalog");
        Console.WriteLine($"RESULT NAVIGATION_MATCH_PASS={count} FAIL=0. Matcher tests only; live catalog authorization/HTTP/browser checks are separate.");
    }
}

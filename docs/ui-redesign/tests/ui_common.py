import os, subprocess, time, json, ssl, urllib.request

ROOT = r"C:\Users\user\Desktop\Orange-Tendering-System\Orange-Tendering-System"
SCR = r"C:\Users\user\AppData\Local\Temp\claude\d55eb3acf3955c2b\4eaa1fea-2fec-464d-89bf-7d0b975904ee\scratchpad"
BASE = "https://localhost:5099"
STATE = json.load(open(os.path.join(SCR, "ui-state.json")))
ACCOUNTS = {a["area"]: a for a in STATE["accounts"]}


def start_server():
    env = dict(os.environ)
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": BASE,
        "ConnectionStrings__Default Connection": STATE["connection"],
        "BootstrapAdmin__Email": ACCOUNTS["Admin"]["email"], "BootstrapAdmin__Password": ACCOUNTS["Admin"]["password"],
    })
    log = open(os.path.join(SCR, "ui-server.log"), "w")
    proc = subprocess.Popen(["dotnet", "bin/Debug/net10.0/Orange-Tendering-System.dll"], cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
    ctx = ssl._create_unverified_context()
    for _ in range(90):
        try:
            urllib.request.urlopen(BASE + "/Identity/Account/Login", context=ctx, timeout=3); return proc
        except Exception:
            time.sleep(1)
    raise RuntimeError("server did not start")


def stop_server(proc):
    proc.terminate()
    try: proc.wait(10)
    except Exception: proc.kill()


def login(page, area):
    a = ACCOUNTS[area]
    page.goto(BASE + "/Identity/Account/Login")
    page.fill("#Input_Email", a["email"]); page.fill("#Input_Password", a["password"])
    page.click("#login-submit")
    page.wait_for_load_state("networkidle")
    return a


def theme(context, mode):
    context.add_init_script("try{localStorage.setItem('orange-ui-theme','%s')}catch(e){}" % mode)

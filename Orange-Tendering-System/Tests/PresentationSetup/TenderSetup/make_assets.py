"""Generates the offer documents (quotation + datasheet PDFs) and handwritten-style signature PNGs used by TenderSetup."""
import json, os, random, sys, datetime
from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "assets")
os.makedirs(out, exist_ok=True)
data = json.load(open(os.path.join(here, "tenders.json"), encoding="utf-8"))
F = "C:/Windows/Fonts/"
def font(name, size): return ImageFont.truetype(F + name, size)

# --- signatures -------------------------------------------------------------------------------
sig_fonts = ["segoesc.ttf", "Inkfree.ttf", "LHANDW.TTF", "FREESCPT.TTF", "MISTRAL.TTF", "BRUSHSCI.TTF"]
people = ["Majed", "Tariq", "Amal", "Saja", "Anfal", "Mohammad", "Batool", "Hadeel", "Sara"]
for i, name in enumerate(people):
    img = Image.new("RGBA", (520, 180), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    f = font(sig_fonts[i % len(sig_fonts)], 84 if sig_fonts[i % len(sig_fonts)] in ("FREESCPT.TTF", "MISTRAL.TTF", "BRUSHSCI.TTF") else 64)
    d.text((30, 30), name, font=f, fill=(20, 40, 110, 255))
    w = d.textlength(name, font=f)
    rnd = random.Random(name)
    y = 132 + rnd.randint(-6, 6)
    d.line([(26, y), (40 + w * 0.55, y + 6), (60 + w, y - 4)], fill=(20, 40, 110, 230), width=3, joint="curve")
    img.save(os.path.join(out, f"signature-{name.lower()}.png"))

# --- offer documents --------------------------------------------------------------------------
A4 = (1240, 1754)  # 150 dpi
def page():
    img = Image.new("RGB", A4, "white"); return img, ImageDraw.Draw(img)

def letterhead(d, sup, colour):
    d.rectangle([0, 0, A4[0], 150], fill=colour)
    d.text((70, 38), sup["company"], font=font("segoeuib.ttf", 46), fill="white")
    d.text((72, 98), f'{sup["address"]}   |   {sup["phone"]}   |   {sup["email"]}', font=font("segoeui.ttf", 22), fill="white")
    d.text((70, A4[1] - 70), f'Commercial registration No. {sup["registration"]}', font=font("segoeui.ttf", 20), fill=(110, 110, 110))

def stamp(d, sup, x, y):
    c = (30, 60, 160)
    d.ellipse([x, y, x + 210, y + 210], outline=c, width=5)
    d.ellipse([x + 16, y + 16, x + 194, y + 194], outline=c, width=2)
    words = sup["company"].upper().split()
    d.text((x + 105, y + 78), " ".join(words[:2]), font=font("segoeuib.ttf", 17), fill=c, anchor="mm")
    d.text((x + 105, y + 108), " ".join(words[2:]) or "CO.", font=font("segoeuib.ttf", 17), fill=c, anchor="mm")
    d.text((x + 105, y + 140), "AMMAN - JORDAN", font=font("segoeui.ttf", 17), fill=c, anchor="mm")

colours = {"ali": (0, 102, 153), "mosa": (120, 30, 60), "omar": (30, 110, 60)}
today = datetime.date.today()
manifest = []
for t in data["tenders"]:
    for n, o in enumerate(t.get("offers", [])):
        sup = data["suppliers"][o["supplier"]]
        col = colours[o["supplier"]]
        qty = t["quantity"]; unit = o["unitPrice"]; subtotal = qty * unit; tax = round(subtotal * 0.16, 2); total = subtotal + tax
        qno = f'Q-{today:%Y%m}-{t["key"][1:]}{n + 1:02d}{o["supplier"][0].upper()}'

        img, d = page(); letterhead(d, sup, col)
        d.text((70, 200), "OFFICIAL QUOTATION", font=font("segoeuib.ttf", 40), fill=(30, 30, 30))
        d.text((70, 260), f"Quotation No.: {qno}", font=font("segoeui.ttf", 26), fill=(40, 40, 40))
        d.text((70, 298), f"Date: {today:%d %B %Y}", font=font("segoeui.ttf", 26), fill=(40, 40, 40))
        d.text((70, 336), "To: Sourcing Department", font=font("segoeui.ttf", 26), fill=(40, 40, 40))
        d.text((70, 374), f'Subject: {t["item"]} - {qty} {t["unit"].lower()}', font=font("segoeui.ttf", 26), fill=(40, 40, 40))
        y = 450
        d.rectangle([70, y, 1170, y + 52], fill=(235, 238, 242))
        for x, h in [(85, "Description"), (690, "Qty"), (800, "Unit price (JOD)"), (1010, "Amount (JOD)")]:
            d.text((x, y + 12), h, font=font("segoeuib.ttf", 22), fill=(30, 30, 30))
        y += 64
        d.text((85, y), o["product"], font=font("segoeuib.ttf", 24), fill=(20, 20, 20))
        desc = o["proposal"]; line = ""; yy = y + 36
        for word in desc.split():
            if d.textlength(line + " " + word, font=font("segoeui.ttf", 21)) > 570: d.text((85, yy), line.strip(), font=font("segoeui.ttf", 21), fill=(70, 70, 70)); yy += 30; line = ""
            line += " " + word
        d.text((85, yy), line.strip(), font=font("segoeui.ttf", 21), fill=(70, 70, 70))
        d.text((700, y), f"{qty}", font=font("segoeui.ttf", 24), fill=(20, 20, 20))
        d.text((820, y), f"{unit:,.2f}", font=font("segoeui.ttf", 24), fill=(20, 20, 20))
        d.text((1020, y), f"{subtotal:,.2f}", font=font("segoeui.ttf", 24), fill=(20, 20, 20))
        y = max(yy + 60, y + 200)
        d.line([70, y, 1170, y], fill=(200, 200, 200), width=2); y += 20
        for label, value in [("Subtotal", subtotal), ("Sales tax 16%", tax), ("Total (JOD)", total)]:
            bold = label.startswith("Total")
            d.text((780, y), label, font=font("segoeuib.ttf" if bold else "segoeui.ttf", 24), fill=(20, 20, 20))
            d.text((1020, y), f"{value:,.2f}", font=font("segoeuib.ttf" if bold else "segoeui.ttf", 24), fill=(20, 20, 20)); y += 40
        y += 40
        d.text((70, y), "Terms and conditions", font=font("segoeuib.ttf", 26), fill=(30, 30, 30)); y += 46
        for term in [f'Delivery: within {o["delivery"]} calendar days from the purchase order.', f'Warranty: {o["warranty"]} months, onsite, parts and labour.',
                     "Payment: 30 days from delivery and acceptance.", "Validity: 90 days from the date of this quotation.", "Prices include delivery, installation and sales tax as shown."]:
            d.text((90, y), "•  " + term, font=font("segoeui.ttf", 23), fill=(50, 50, 50)); y += 38
        y += 50
        d.text((70, y), "Authorised signature", font=font("segoeui.ttf", 22), fill=(90, 90, 90))
        d.text((70, y + 30), sup["signer"], font=font("segoesc.ttf", 54), fill=(20, 40, 110))
        d.text((70, y + 110), f'{sup["signer"]} - Sales Manager', font=font("segoeui.ttf", 22), fill=(60, 60, 60))
        stamp(d, sup, 880, y - 10)
        qpath = os.path.join(out, f'{t["key"]}-{o["supplier"]}-quotation.pdf'); img.save(qpath, "PDF", resolution=150)

        img, d = page(); letterhead(d, sup, col)
        d.text((70, 200), "PRODUCT DATASHEET", font=font("segoeuib.ttf", 40), fill=(30, 30, 30))
        d.text((70, 262), o["product"], font=font("segoeuib.ttf", 34), fill=col)
        d.text((70, 316), f'Offered for: {t["item"]}', font=font("segoeui.ttf", 24), fill=(70, 70, 70))
        y = 390
        d.text((70, y), "Required specification", font=font("segoeuib.ttf", 26), fill=(30, 30, 30)); d.text((680, y), "Offered", font=font("segoeuib.ttf", 26), fill=(30, 30, 30)); y += 50
        for spec in t["specifications"].split("\n"):
            d.rectangle([70, y - 6, 1170, y + 44], outline=(225, 225, 225))
            d.text((85, y), spec, font=font("segoeui.ttf", 21), fill=(40, 40, 40))
            d.text((690, y), "Complies", font=font("segoeuib.ttf", 21), fill=(30, 120, 60)); y += 56
        y += 30
        d.text((70, y), "Product description", font=font("segoeuib.ttf", 26), fill=(30, 30, 30)); y += 44
        line = ""
        for word in o["proposal"].split():
            if d.textlength(line + " " + word, font=font("segoeui.ttf", 22)) > 1080: d.text((70, y), line.strip(), font=font("segoeui.ttf", 22), fill=(60, 60, 60)); y += 32; line = ""
            line += " " + word
        d.text((70, y), line.strip(), font=font("segoeui.ttf", 22), fill=(60, 60, 60))
        dpath = os.path.join(out, f'{t["key"]}-{o["supplier"]}-datasheet.pdf'); img.save(dpath, "PDF", resolution=150)
        manifest.append(qpath); manifest.append(dpath)

print(f"{len(people)} signatures and {len(manifest)} offer documents written to {out}")

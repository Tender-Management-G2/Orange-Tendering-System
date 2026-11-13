// Runs against an isolated, migrated Authentication test database and the local DevMail server.
const crypto = require('node:crypto');
const base = process.env.AUTH_TEST_URL || 'http://127.0.0.1:5266';
const suffix = crypto.randomBytes(5).toString('hex');
const email = `supplier-${suffix}@example.test`;
const password = `A!a1${crypto.randomBytes(16).toString('hex')}`;
const cookieJar = new Map();
const cookies = () => [...cookieJar].map(([name, value]) => `${name}=${value}`).join('; ');
function saveCookies(response) {
  for (const header of response.headers.getSetCookie()) {
    const pair = header.split(';', 1)[0];
    const separator = pair.indexOf('=');
    cookieJar.set(pair.slice(0, separator), pair.slice(separator + 1));
  }
}

async function page(path) {
  const response = await fetch(base + path, { redirect: 'manual', headers: { Cookie: cookies() } });
  if (response.status !== 200) throw new Error(`GET ${path} returned ${response.status}`);
  saveCookies(response);
  const html = await response.text();
  const token = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  if (!token) throw new Error(`No antiforgery token on ${path}`);
  return token[1];
}

async function main() {
const token = await page('/Identity/Account/Register');
const form = new FormData();
for (const [key, value] of Object.entries({
  'Input.FullName': 'Development Test Contact',
  'Input.Email': email,
  'Input.Password': password,
  'Input.ConfirmPassword': password,
  'Input.CompanyName': `Development Test Supplier ${suffix}`,
  'Input.Category': 'Other',
  'Input.RegistrationNumber': `DEV-${suffix}`,
  'Input.CompanyEmail': `company-${suffix}@example.test`,
  'Input.CompanyPhone': '+962790000000',
  'Input.CompanyAddress': 'Amman, Jordan',
  '__RequestVerificationToken': token
})) form.append(key, value);
const pdf = new Blob(['%PDF-1.7\nTest legal document\n%%EOF'], { type: 'application/pdf' });
form.append('CommercialRegistrationCertificate', pdf, 'registration.pdf');
form.append('LegalDocuments', pdf, 'license.pdf');
const registration = await fetch(base + '/Identity/Account/Register', {
  method: 'POST', body: form, redirect: 'manual', headers: { Cookie: cookies() }
});
if (registration.status !== 302 || !registration.headers.get('location')?.includes('VerifyEmail')) {
  const html = await registration.text();
  const errors = [...html.matchAll(/class="[^"]*(?:validation-summary|field-error)[^"]*"[^>]*>([^<]+)/g)].map(match => match[1]);
  throw new Error(`Registration returned ${registration.status}: ${errors.join('; ')}`);
}

const messages = await (await fetch('http://127.0.0.1:8025/api/messages')).json();
const message = messages.find(item => item.to.includes(email));
if (!message) throw new Error('OTP email did not reach local DevMail inbox');
const code = message.raw.match(/\b\d{6}\b/)?.[0];
if (!code) throw new Error('OTP email did not contain a six-digit code');

const verifyToken = await page('/Identity/Account/VerifyEmail?email=' + encodeURIComponent(email));
const verification = await fetch(base + '/Identity/Account/VerifyEmail', {
  method: 'POST', redirect: 'manual', headers: {
    Cookie: cookies(),
    'Content-Type': 'application/x-www-form-urlencoded'
  },
  body: new URLSearchParams({
    'Input.Email': email, 'Input.Code': code, '__RequestVerificationToken': verifyToken
  })
});
const verificationHtml = await verification.text();
if (!verificationHtml.includes('Email verified. Your company is awaiting administrator approval.')) {
  const errors = [...verificationHtml.matchAll(/class="[^"]*(?:validation-summary|field-error)[^"]*"[^>]*>([^<]+)/g)].map(match => match[1]);
  throw new Error(`OTP verification returned ${verification.status}; allow=${verification.headers.get('allow')}; title=${verificationHtml.match(/<title>([^<]+)/)?.[1]}; errors=${errors.join('; ')}`);
}

process.stdout.write('PASS supplier registration, local SMTP delivery, and OTP verification\n');
}

main().catch(error => { process.stderr.write(error.message + '\n'); process.exitCode = 1; });

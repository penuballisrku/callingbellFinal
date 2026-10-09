"""End-to-end checks for one real mobile number: OTP sign-in and the notifications that number receives.

    python tests/api/phone_flow.py --phone 98XXXXXXXX                 # development: the API returns the code
    python tests/api/phone_flow.py --phone 98XXXXXXXX --interactive   # real SMS/WhatsApp: type the code you received
    python tests/api/phone_flow.py --phone 98XXXXXXXX --skip-booking  # OTP only

The number is never written to the repo; pass it on the command line or in CB_TEST_PHONE.
What it does:
  1. Finds the account that owns the number (sign-in needs one; register at /register first otherwise).
  2. Asks for a SIGNUP code for that registered number: the answer must look the same but no code may be sent (no account probing).
  3. Requests a LOGIN code, shows how it was delivered (WhatsApp, SMS provider, or the development log), rejects a wrong code,
     then signs in with the right one.
  4. Books an online consultation at a demo business as that customer; the owner confirms it. That queues the BOOKING and VIDEO
     notifications to this number (web push, then WhatsApp) and opens the booking's video room. The chain each one took is printed.
  5. Cancels the booking. tests/sql/CleanupTestData.sql removes it and its notifications.
Limits to keep in mind: 3 codes per number every 15 minutes (5 an hour), 10 per IP every 15 minutes, 30 seconds between codes.
"""
import argparse
import datetime
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'lib'))
from cbtest import TEST_MARK, call, check, finish, info, login, section, skip, sql  # noqa: E402

VIDEO_OWNER = 'jignesh.desai@demo.callingbell.in'   # Akhada Fitness Studio: offers video consultations
VIDEO_BUSINESS_SLUG = 'akhada-fitness-studio'

parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument('--phone', default=os.environ.get('CB_TEST_PHONE'), help='10-digit Indian mobile number (or +91...)')
parser.add_argument('--interactive', action='store_true', help='prompt for the code when the API does not return it')
parser.add_argument('--skip-booking', action='store_true', help='only test OTP sign-in')
args = parser.parse_args()
if not args.phone:
    parser.error('pass --phone or set CB_TEST_PHONE')
digits = re.sub(r'\D', '', args.phone)[-10:]
if not re.fullmatch(r'[6-9]\d{9}', digits):
    parser.error('enter a valid 10-digit Indian mobile number')
masked = f'+91 •••••{digits[-5:]}'


def otp_rows():
    return sql(f"SELECT CAST(Id AS nvarchar(36)), Purpose FROM dbo.OtpCodes WHERE REPLACE(PhoneNumber,' ','') = '+91{digits}' ORDER BY CreatedAt DESC")


def deliveries(where):
    return sql("SELECT RouteStep, Channel, Provider, Status, ISNULL(ErrorCode,''), ISNULL(LEFT(ErrorMessage,90),''), ISNULL(Recipient,'') "
               f"FROM dbo.NotificationDeliveries WHERE {where} ORDER BY RouteStep, CreatedAt")


def show_chain(rows):
    for step, channel, provider, status, code, error, recipient in rows:
        info(f'step {step}: {channel:<24} {provider:<8} {status:<12} {recipient} {code} {error}'.rstrip())


# ---------------- 1. the account ----------------
section(f'Account for {masked}')
account = sql("SELECT CAST(Id AS nvarchar(450)), Email, ISNULL(DisplayName,''), UserType, IsActive FROM dbo.AspNetUsers "
              f"WHERE IsDeleted = 0 AND REPLACE(PhoneNumber,' ','') = '+91{digits}'")
if not check('the number belongs to an account', len(account) == 1, f'{len(account)} account(s)'):
    info('Register it first: open /register, choose "Sign up with mobile", or add it to an account under Account settings.')
    finish()
user_id, email, name, user_type, active = account[0]
info(f'{name} <{email}>, {user_type}' + ('' if active == '1' else ' (inactive)'))

# ---------------- 2. no account probing ----------------
section('SIGNUP code for a registered number')
before = len(otp_rows())
st, r, _ = call('POST', '/api/auth/request-otp', {'phoneNumber': digits, 'purpose': 'SIGNUP'})
check('same generic answer as for any number', st == 200 and r['data']['developmentCode'] is None, r.get('message') if isinstance(r, dict) else st)
check('no code was generated or sent', len(otp_rows()) == before)

# ---------------- 3. LOGIN code ----------------
section('LOGIN code')
before = otp_rows()
st, r, _ = call('POST', '/api/auth/request-otp', {'phoneNumber': digits, 'purpose': 'LOGIN'})
check('request accepted', st == 200, st)
after = otp_rows()
code = r['data']['developmentCode'] if st == 200 else None
token = None
if len(after) == len(before):
    skip('code delivery', 'no new code: the per-number limit (3 per 15 min, 30 s apart) or the IP limit is reached; try again later')
else:
    otp_id = after[0][0]
    chain = deliveries(f"OtpCodeId = '{otp_id}'")
    show_chain(chain)
    delivered = [c for c in chain if c[3] in ('Accepted', 'Sent', 'Delivered', 'Read')]
    check('a channel accepted the code', delivered, delivered and f'{delivered[-1][1]} via {delivered[-1][2]}')
    if delivered and delivered[-1][2] == 'Log':
        info('The development "Log" SMS provider took it: nothing reached the phone. Configure WhatsApp or MSG91/Twilio to receive it.')
    if code is None and args.interactive:
        code = input(f'     Enter the code sent to {masked}: ').strip()
    if code is None:
        skip('sign in with the code', 'the API does not return codes here (production settings); re-run with --interactive')
    else:
        wrong = '000000' if code != '000000' else '111111'
        st, r, _ = call('POST', '/api/auth/verify-otp', {'phoneNumber': digits, 'code': wrong, 'purpose': 'LOGIN'})
        check('a wrong code is rejected', st == 400, st)
        st, r, _ = call('POST', '/api/auth/verify-otp', {'phoneNumber': digits, 'code': code, 'purpose': 'LOGIN'})
        auth = r['data']['auth'] if st == 200 else None
        check('the right code signs in to that account', auth and auth['user']['email'].lower() == email.lower(), auth and auth['user']['email'])
        token = auth and auth['accessToken']
        st, r, _ = call('POST', '/api/auth/verify-otp', {'phoneNumber': digits, 'code': code, 'purpose': 'LOGIN'})
        check('the same code cannot be used twice', st == 400, st)

# ---------------- 4. booking → notifications to this number ----------------
if args.skip_booking:
    finish()
section('Booking confirmed: notifications to this number')
if not token:
    skip('booking notifications', 'needs the OTP sign-in above')
    finish()
if user_type != 'Customer':
    skip('booking notifications', f'the account is a {user_type}; bookings are made by customers')
    finish()

page = call('GET', f'/api/businesses/{VIDEO_BUSINESS_SLUG}')[1]['data']
business_id = page['card']['id']
service = next(s for s in page['services'] if s['type'] == 'Online')
slot = None
for days in range(1, 8):
    day = (datetime.date.today() + datetime.timedelta(days=days)).isoformat()
    st, r, _ = call('GET', f"/api/businesses/{business_id}/slots?serviceId={service['id']}&date={day}")
    slot = next((s for s in r['data'] if s['available']), None) if st == 200 else None
    if slot:
        break
if not check('found a free slot', slot, slot and slot['start']):
    finish()
st, r, _ = call('POST', '/api/me/bookings', {'businessId': business_id, 'serviceId': service['id'], 'scheduledStart': slot['start'],
                                                    'serviceAddress': None, 'notes': f'Phone notification test {TEST_MARK}', 'contactPhone': None}, token=token)
booking = r['data'] if st == 200 else None
if not check(f"booked {service['name']} at {page['card']['name']}", booking, booking and booking['reference'] or r):
    finish()
owner = login(VIDEO_OWNER)
st, r, _ = call('PATCH', f"/api/owner/businesses/{business_id}/bookings/{booking['id']}", {'status': 'Confirmed', 'reason': None}, token=owner)
check('owner confirms it', st == 200, st)

# The dispatcher is a background service: give it a few seconds.
rows = []
for _ in range(20):
    rows = sql("SELECT CAST(Id AS nvarchar(36)), RouteCode, DispatchStatus FROM dbo.Notifications "
               f"WHERE UserId = '{user_id}' AND RouteCode IS NOT NULL AND (ReferenceId = '{booking['id']}' "
               f"OR ReferenceId IN (SELECT Id FROM dbo.VideoRooms WHERE BookingId = '{booking['id']}'))")
    if rows and all(s not in ('Pending', 'Processing') for _, _, s in rows):
        break
    time.sleep(1)
routes = {route for _, route, _ in rows}
check('BOOKING (confirmed) and VIDEO notifications queued for this customer', {'BOOKING', 'VIDEO'} <= routes, sorted(routes))
for nid, route, status in rows:
    info(f'{route}: {status}')
    show_chain(deliveries(f"NotificationId = '{nid}'"))
    if not deliveries(f"NotificationId = '{nid}'"):
        info('  (no attempts yet: the dispatcher picks it up shortly)')
st, r, _ = call('GET', f"/api/video/bookings/{booking['id']}/room", token=token)
check('the confirmed booking has a private video room', st == 200, r['data'] if st == 200 else st)

st, r, _ = call('POST', f"/api/me/bookings/{booking['id']}/cancel", {'reason': f'Automated test {TEST_MARK}'}, token=token)
check('the test booking is cancelled', st == 200, st)
finish()

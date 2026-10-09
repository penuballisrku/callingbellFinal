"""API checks for chat, team (staff) and video consultations.

    python tests/api/phase2_api.py            # API at CB_API (default http://localhost:5080)

Uses the seeded demo accounts. Everything it creates is tagged [cb-test]; run tests/sql/CleanupTestData.sql afterwards
(RunAllTests.ps1 does).
"""
import datetime
import os
import sys
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'lib'))
from cbtest import TEST_MARK, call, check, finish, info, login, section, sql  # noqa: E402

CUSTOMER = 'naveen.menon@demo.callingbell.in'
OWNER = 'nagaraj.gowda@demo.callingbell.in'          # GreenLeaf Gardening Services (no video)
VIDEO_OWNER = 'jignesh.desai@demo.callingbell.in'    # Akhada Fitness Studio (offers video)
ADMIN = 'admin@demo.callingbell.in'

cust, own, vown, admin = login(CUSTOMER), login(OWNER), login(VIDEO_OWNER), login(ADMIN)
business = call('GET', '/api/businesses/greenleaf-gardening-services')[1]['data']['card']['id']
video_business = call('GET', '/api/businesses/akhada-fitness-studio')[1]['data']['card']['id']

# ---------------- chat ----------------
section('Chat')
st, r, _ = call('POST', '/api/chat/conversations', {'businessId': business}, token=cust)
check('customer opens (or reuses) a conversation with the business', st == 200, st)
conv = r['data']['id']
st, r, _ = call('GET', '/api/chat/conversations', token=cust)
check('customer sees their conversations', st == 200 and any(c['id'] == conv for c in r['data']), f"{len(r['data'])} conversation(s)")
st, r, _ = call('GET', f'/api/chat/conversations?role=Business&businessId={business}', token=own)
mine = next((c for c in r['data'] if c['id'] == conv), None) if st == 200 else None
check('owner sees it on the business side', mine and mine['myRole'] == 'Business', mine and mine['customerName'])
st, r, _ = call('GET', f'/api/chat/conversations/{conv}/messages', token=admin)
check('someone outside the conversation is refused', st == 403, st)

st, r, _ = call('POST', f'/api/chat/conversations/{conv}/messages', {'body': f'Line one\nLine two \u0007 with a bell char {TEST_MARK}'}, token=cust)
msg = r['data'] if st == 200 else {}
check('send keeps line breaks, strips control characters', st == 200 and msg['body'] == f'Line one\nLine two  with a bell char {TEST_MARK}', repr(msg.get('body')))
st, r, _ = call('GET', '/api/chat/unread', token=own)
check('owner unread count went up', st == 200 and r['data']['asBusiness'] >= 1, r['data'] if st == 200 else st)
call('POST', f'/api/chat/conversations/{conv}/read', token=own)
st, r, _ = call('GET', f'/api/chat/conversations/{conv}/messages', token=cust)
check("read receipt recorded on the customer's message", st == 200 and any(m['id'] == msg.get('id') and m['readAt'] for m in r['data']))
st, r, _ = call('POST', f'/api/chat/conversations/{conv}/messages', {'body': '   '}, token=cust)
check('empty message rejected', st == 400, st)

png = bytes.fromhex('89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4890000000D49444154789C6360000002000154A24F5D0000000049454E44AE426082')
boundary = 'cbboundary' + uuid.uuid4().hex
form = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="site photo.png"\r\nContent-Type: image/png\r\n\r\n').encode() + png + \
       (f'\r\n--{boundary}\r\nContent-Disposition: form-data; name="caption"\r\n\r\nPhoto of the switchboard {TEST_MARK}\r\n--{boundary}--\r\n').encode()
st, r, _ = call('POST', f'/api/chat/conversations/{conv}/attachments', raw=form, token=cust, ctype=f'multipart/form-data; boundary={boundary}')
att = r['data']['attachment'] if st == 200 else None
check('image attachment sent', st == 200 and att and att['isImage'], att and att['name'])
if att:
    st, data, h = call('GET', att['url'])
    check('signed link serves the image', st == 200 and data == png and h.get('Content-Type') == 'image/png', st)
    st, _, _ = call('GET', att['url'].replace('sig=', 'sig=x'))
    check('tampered link refused', st in (401, 403), st)
    media = sql(f"SELECT CAST(AttachmentMediaId AS nvarchar(36)) FROM dbo.ChatMessages WHERE Id='{r['data']['id']}'")[0][0]
    st, _, _ = call('GET', f'/api/media/{media.lower()}')
    check('attachment is not served by the public media URL', st == 404, st)
exe = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="x.exe"\r\nContent-Type: application/octet-stream\r\n\r\nMZ\x90\x00\r\n--{boundary}--\r\n').encode()
st, r, _ = call('POST', f'/api/chat/conversations/{conv}/attachments', raw=exe, token=cust, ctype=f'multipart/form-data; boundary={boundary}')
check('non-image, non-PDF file rejected', st == 400, st)

# ---------------- staff ----------------
section('Team (staff)')
st, r, _ = call('GET', f'/api/owner/businesses/{business}/staff', token=own)
team = r['data'] if st == 200 else []
check('owner sees the team with workload', st == 200 and len(team) >= 1 and 'upcomingBookings' in team[0], f'{len(team)} member(s)')
st, r, _ = call('GET', f'/api/businesses/{business}/team')
check('public team has no phone or email', st == 200 and r['data'] and 'phone' not in r['data'][0] and 'email' not in r['data'][0])
st, r, _ = call('GET', f'/api/owner/businesses/{business}/staff', token=cust)
check('a customer cannot see the owner team view', st == 403, st)

services = call('GET', f'/api/owner/businesses/{business}/services', token=own)[1]['data']
svc = services[0] if isinstance(services, list) else services['items'][0]
new = {'fullName': 'Test Member', 'title': 'Trainee', 'phone': '9876512345', 'email': 'trainee@example.com', 'bio': TEST_MARK, 'yearsExperience': 1,
       'languages': 'Telugu, English', 'acceptsBookings': True, 'isActive': True, 'serviceIds': [svc['id']],
       'hours': [{'dayOfWeek': d, 'open': '10:00', 'close': '18:00', 'isClosed': d == 0} for d in range(7)]}
st, r, _ = call('POST', f'/api/owner/businesses/{business}/staff', new, token=own)
added = r['data'] if st == 200 else {}
check('add a team member with hours and services', st == 200 and added['phone'] == '+91 98765 12345' and len(added['hours']) == 7, st)
st, r, _ = call('POST', f'/api/owner/businesses/{business}/staff', {**new, 'hours': [{'dayOfWeek': 1, 'open': '18:00', 'close': '10:00', 'isClosed': False}]}, token=own)
check('closing before opening rejected', st == 400, st)
if added:
    today = datetime.date.today()
    day = (today + datetime.timedelta(days=(1 - today.weekday()) % 7 or 7)).isoformat()  # next Tuesday
    st, r, _ = call('GET', f"/api/businesses/{business}/slots?serviceId={svc['id']}&date={day}&staffId={added['id']}")
    free = [s['time'] for s in r['data'] if s['available']] if st == 200 else []
    check("slots for one person follow their hours (10:00-18:00)", free and min(free) >= '10:00', f"{len(free)} free, {free[:1]} to {free[-1:]}")
    st, r, _ = call('DELETE', f"/api/owner/businesses/{business}/staff/{added['id']}", token=own)
    check('remove the team member', st == 200, st)

# ---------------- video ----------------
section('Video consultation')
st, r, _ = call('GET', f'/api/video/rooms/{uuid.uuid4()}', token=cust)
check('unknown video room: 404', st == 404, st)
st, r, _ = call('POST', f'/api/chat/conversations/{conv}/video', token=cust)
check('no video call where the business does not offer video', st == 400, f"{st} {r.get('message', '')[:60] if isinstance(r, dict) else ''}")
vconv = call('POST', '/api/chat/conversations', {'businessId': video_business}, token=cust)[1]['data']['id']
st, r, _ = call('POST', f'/api/chat/conversations/{vconv}/video', token=cust)
check('customer starts a video call from chat (business offers video)', st == 200, st)
if st == 200:
    room = r['data']
    st, r, _ = call('GET', f'/api/video/rooms/{room}', token=vown)
    ok = st == 200 and r['data']['canJoinNow'] and r['data']['iceServers']
    check('owner can open the room, with ICE (STUN/TURN) servers', ok, r['data']['myRole'] if st == 200 else st)
    st, r, _ = call('GET', f'/api/video/rooms/{room}', token=admin)
    check('outsider refused from the video room', st == 403, st)
    st, r, _ = call('POST', f'/api/video/rooms/{room}/end', token=vown)
    check('owner ends the call', st == 200, st)
    info(f'room /video/{room}')

finish()

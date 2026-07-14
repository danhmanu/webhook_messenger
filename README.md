# Messenger OpenAI Webhook

Ung dung ASP.NET Core dung lam webhook cho Facebook Messenger. Khi nguoi dung nhan tin den Page, app nhan webhook, goi OpenAI de tao cau tra loi, sau do gui lai tin nhan qua Facebook Graph API.

Ung dung gom backend ASP.NET Core controller-based REST API/webhook va frontend quan tri React TypeScript trong thu muc `admin_frontend`. Backend va frontend chay thanh 2 service rieng, du lieu duoc luu bang SQLite trong thu muc `data/`.

## Tinh nang

- Xac minh webhook Facebook qua `GET /webhook`.
- Nhan tin nhan Messenger qua `POST /webhook`.
- Kiem tra chu ky `X-Hub-Signature-256` neu cau hinh `MessengerAppSecret`.
- Bo qua event he thong nhu `delivery`, `read`, `reaction`, `is_echo`.
- Ho tro text, quick reply, postback va attachment fallback.
- Goi OpenAI Responses API de tao cau tra loi.
- Gui typing indicator va tin nhan tra loi qua Graph API.
- Trang quan tri React TypeScript chay rieng tai `admin.vietnamhospital.cloud`.
- Luu hoi thoai, tin khach gui va tin bot/admin tra loi bang SQLite.
- AI Agent tu dieu phoi truoc khi tra loi: chat model, memory SQLite va tool SQLite.
- API RESTful versioned tai `/api/v1`.
- API quan ly snippet: `GET/POST/PUT/PATCH/DELETE /api/v1/message-snippets`.
- API hop thu: `GET /api/v1/conversations`, `GET/POST /api/v1/conversations/{senderId}/messages`.
- Log ro khi co message den, khi bo qua event, khi loi OpenAI/Messenger API.

## Yeu cau

- Domain HTTPS tro ve server, vi Facebook webhook yeu cau HTTPS.
- Facebook App da bat Messenger product.
- Facebook Page access token.
- OpenAI API key.
- Neu deploy bang Docker: Ubuntu server co Docker, Nginx va Certbot.

## Bien moi truong

Tao file `.env` tu file mau:

```bash
cp .env.example .env
nano .env
```

Noi dung can cau hinh:

```env
App__OpenAiApiKey=sk-your-openai-api-key
App__OpenAiModel=gpt-4o-mini
App__MessengerVerifyToken=your-random-verify-token
App__MessengerPageAccessToken=your-facebook-page-access-token
App__MessengerAppSecret=your-facebook-app-secret
App__MessengerGraphApiVersion=v25.0
App__AdminUsername=admin
App__AdminPassword=change-this-password
App__AdminSessionHours=8
App__SystemPrompt=Ban la tro ly AI than thien cho fanpage Messenger. Tra loi ngan gon, tu nhien bang tieng Viet.
App__CorsOrigins__0=https://admin.vietnamhospital.cloud
FRONTEND_ORIGIN=https://admin.vietnamhospital.cloud
VITE_API_BASE=https://vietnamhospital.cloud/api/v1
```

Ghi chu:

- `App__MessengerVerifyToken`: token tu dat, dung de Facebook xac minh webhook.
- `App__MessengerPageAccessToken`: Page Access Token dung de gui tin nhan.
- `App__MessengerAppSecret`: App Secret cua Facebook App, dung de kiem tra chu ky webhook.
- `App__AdminUsername`: ten dang nhap de seed tai khoan admin dau tien vao SQLite khi bang `admin_users` con trong.
- `App__AdminPassword`: mat khau de seed tai khoan admin dau tien vao SQLite. App luu trong database bang hash PBKDF2, khong luu plain text.
- `App__AdminSessionHours`: so gio hieu luc cua phien dang nhap admin. Mac dinh la 8 gio.
- Khong nen commit `.env` hoac secret that len Git.

## Chay local

Chay backend API/webhook:

```bash
dotnet restore
dotnet run --urls http://127.0.0.1:5035
```

Kiem tra:

```bash
curl http://127.0.0.1:5035/health
```

Mo trang quan tri local:

```bash
cd admin_frontend
npm install
npm run dev
```

Vite dev server se proxy `/api`, `/health`, `/webhook` ve backend local `http://127.0.0.1:5035`.

URL frontend local:

```text
http://127.0.0.1:5173
```

Build frontend production:

```bash
cd admin_frontend
npm run build
```

Ket qua build nam trong `admin_frontend/wwwroot/`.

## Deploy Ubuntu bang Docker

SSH vao server:

```bash
ssh root@YOUR_SERVER_IP
```

Cai goi can thiet:

```bash
apt update && apt upgrade -y
apt install -y ca-certificates curl git nginx
```

Cai Docker:

```bash
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc

echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" \
  > /etc/apt/sources.list.d/docker.list

apt update
apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Clone source:

```bash
cd /opt
git clone https://github.com/danhmanu/webhook_messenger.git
cd /opt/webhook_messenger
```

Tao `.env`:

```bash
nano .env
```

Build va chay 2 service:

```bash
docker compose up -d --build
```

Mac dinh `docker-compose.yml` se chay:

```text
backend        -> http://127.0.0.1:8080
admin_frontend -> http://127.0.0.1:3000
```

Kiem tra backend trong server:

```bash
curl http://127.0.0.1:8080/health
```

## Cau hinh Nginx va HTTPS

Vi du domain backend la `vietnamhospital.cloud`, domain frontend admin la `admin.vietnamhospital.cloud`.

Tao file Nginx:

```bash
nano /etc/nginx/sites-available/webhook-messenger
```

Noi dung:

```nginx
server {
    listen 80;
    server_name vietnamhospital.cloud;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;

        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

server {
    listen 80;
    server_name admin.vietnamhospital.cloud;

    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;

        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Bat site:

```bash
ln -s /etc/nginx/sites-available/webhook-messenger /etc/nginx/sites-enabled/
nginx -t
systemctl reload nginx
```

Cai HTTPS:

```bash
apt install -y certbot python3-certbot-nginx
certbot --nginx -d vietnamhospital.cloud -d admin.vietnamhospital.cloud
```

Kiem tra:

```bash
curl https://vietnamhospital.cloud/health
```

## Cau hinh Facebook Webhook

Trong Facebook Developer, vao Messenger Webhooks va dien:

```text
Callback URL:
https://vietnamhospital.cloud/webhook

Verify Token:
gia tri cua App__MessengerVerifyToken
```

Tat tuy chon:

```text
Dinh kem chung thuc may khach vao yeu cau Webhook
```

App hien tai khong cau hinh mTLS/client certificate.

Test verify truoc bang curl:

```bash
curl "https://vietnamhospital.cloud/webhook?hub.mode=subscribe&hub.verify_token=YOUR_VERIFY_TOKEN&hub.challenge=abc123"
```

Neu dung, ket qua phai la:

```text
abc123
```

Sau khi verify thanh cong, subscribe cac event thuong dung:

```text
messages
messaging_postbacks
message_deliveries
message_reads
```

## Xem log khi co message

Xem realtime:

```bash
docker logs -f webhook-messenger-backend
```

Xem 100 dong cuoi:

```bash
docker logs --tail 100 webhook-messenger-backend
```

Khi co message den, log se co dang:

```text
Received Messenger webhook with 1 entries
Handling Messenger message from sender USER_ID: noi dung tin nhan
Sent Messenger reply to sender USER_ID: noi dung tra loi
```

Neu event bi bo qua:

```text
Ignored Messenger event delivery from sender USER_ID
Ignored Messenger event read from sender USER_ID
Ignored Messenger event message_echo from sender USER_ID
```

Neu thay `Received Messenger webhook...` nhung khong thay reply, xem dong loi ngay sau do. Thuong la sai OpenAI key, sai Page Access Token, Page chua subscribe webhook, hoac Graph API tu choi quyen gui tin.

## Trang quan tri tin nhan mau

Mo:

```text
https://admin.vietnamhospital.cloud
```

Dang nhap bang tai khoan admin duoc luu trong SQLite. Khi database chua co user admin, app se tao user dau tien tu `App__AdminUsername` va `App__AdminPassword` trong `.env`. Token dang nhap duoc sinh ngau nhien, luu hash trong bang `admin_sessions` va het han theo `App__AdminSessionHours`.

Chuc nang:

- Hop thu theo tung sender id, gan giong trang tin nhan Facebook.
- Xem tin khach gui va tin bot/admin tra loi theo dang bong chat.
- Gui tra loi thu cong tu trang quan tri.
- Them doan tin nhan mau.
- Sua tieu de, ma goi nho, noi dung.
- Bat/tat doan tin.
- Xoa doan tin.
- Tim kiem theo tieu de, ma goi nho, noi dung.

Du lieu SQLite duoc luu vao:

```text
data/messenger-webhook.db
```

File nay la du lieu runtime va da duoc ignore khoi Git. Bang `admin_users` luu tai khoan admin, trong do mat khau duoc hash PBKDF2. Bang `admin_sessions` luu hash token dang nhap, thoi diem het han va trang thai thu hoi. Khi doi mat khau, cac phien dang nhap hien tai se bi thu hoi va nguoi dung can dang nhap lai. Neu truoc do co file `data/message-snippets.json`, app se import snippet cu vao SQLite khi database moi chua co snippet.

## AI Agent

Webhook hien goi `AgentService` truoc khi gui phan hoi Messenger.

Luong xu ly:

```text
Tin Messenger
-> WebhookController
-> luu inbound message vao SQLite
-> AgentService.ProcessAsync
   -> doc recent conversation
   -> doc agent_memories
   -> tim snippet phu hop
   -> goi OpenAI chat model de ra JSON decision
   -> chay tool SQLite neu model yeu cau
   -> goi model lan 2 neu co tool output
   -> luu memories/tool logs
-> gui reply qua Messenger
-> luu outbound message vao SQLite
```

Bang SQLite lien quan:

```text
agent_memories
agent_tool_calls
```

Tool noi bo hien co:

```text
search_snippets
get_memory
get_conversation_history
save_memory
```

Endpoint debug theo tung sender:

```text
GET /api/v1/conversations/{senderId}/agent-memories
GET /api/v1/conversations/{senderId}/agent-tool-calls
```

## Cap nhat app tren server

```bash
cd /opt/webhook_messenger
git pull
docker compose up -d --build
```

Kiem tra lai:

```bash
curl https://vietnamhospital.cloud/health
docker logs --tail 100 webhook-messenger-backend
```

## Loi thuong gap

### Facebook bao khong the xac thuc URL

Kiem tra:

```bash
curl "https://vietnamhospital.cloud/webhook?hub.mode=subscribe&hub.verify_token=YOUR_VERIFY_TOKEN&hub.challenge=abc123"
```

Neu khong tra `abc123`:

- Verify token tren Facebook khong khop `App__MessengerVerifyToken`.
- Domain chua tro dung server.
- Nginx chua proxy ve container.
- Container chua chay.
- Dang bat tuy chon client certificate/mTLS tren Facebook.

### Khong thay log khi nhan tin

Kiem tra:

```bash
docker logs -f webhook-messenger-backend
```

Neu khong co dong `Received Messenger webhook...`:

- Facebook chua verify webhook.
- Page chua subscribe webhook.
- URL callback sai.
- Domain HTTPS loi.
- Nginx/firewall chan request vao backend.

### Co log nhung bot khong tra loi

Kiem tra:

- `App__OpenAiApiKey` co dung khong.
- `App__MessengerPageAccessToken` co dung va con hieu luc khong.
- Page Access Token co quyen gui tin nhan khong.
- Facebook App/Page da duoc cau hinh dung chua.
- Xem loi chi tiet bang `docker logs --tail 100 webhook-messenger-backend`.

### Thieu chu ky webhook

Neu cau hinh `App__MessengerAppSecret`, app se yeu cau header `X-Hub-Signature-256`. Request tu Facebook that se co header nay. Neu test thu cong bang curl, co the tam thoi de trong `App__MessengerAppSecret` hoac tao chu ky HMAC dung.

## Endpoint nhanh

```text
GET  /health
GET  /webhook
POST /webhook
GET  /api/v1/health
GET  /api/v1/message-snippets
GET  /api/v1/message-snippets/{id}
POST /api/v1/message-snippets
PUT  /api/v1/message-snippets/{id}
PATCH /api/v1/message-snippets/{id}/activation
DELETE /api/v1/message-snippets/{id}
GET  /api/v1/conversations
GET  /api/v1/conversations/{senderId}/messages
POST /api/v1/conversations/{senderId}/messages
GET  /api/v1/conversations/{senderId}/agent-memories
GET  /api/v1/conversations/{senderId}/agent-tool-calls
```

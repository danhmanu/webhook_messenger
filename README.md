# Messenger OpenAI Webhook (.NET)

Webhook ASP.NET Core Minimal API cho Facebook Messenger chatbox AI, dùng model `gpt-4o-mini`.

## Cau hinh

Dat cac bien moi truong:

```powershell
$env:App__OpenAiApiKey="sk-your-openai-api-key"
$env:App__OpenAiModel="gpt-4o-mini"
$env:App__MessengerVerifyToken="your-random-verify-token"
$env:App__MessengerPageAccessToken="your-facebook-page-access-token"
$env:App__SystemPrompt="Ban la tro ly AI than thien cho fanpage Messenger. Tra loi ngan gon, tu nhien bang tieng Viet."
```

Hoac dien trong `appsettings.json` khi test local. Khi deploy production, nen dung environment variables thay vi commit secret vao source.

## Chay local

```powershell
dotnet run
```

Mac dinh app co cac endpoint:

- `GET /` kiem tra app dang chay.
- `GET /webhook` Facebook dung de verify webhook.
- `POST /webhook` Facebook gui message event.

Neu chay local, dung ngrok hoac Cloudflare Tunnel:

```powershell
ngrok http http://localhost:5000
```

Sau do lay HTTPS URL, vi du:

```text
https://your-ngrok-url.ngrok-free.app/webhook
```

## Cau hinh Facebook Messenger

1. Tao Facebook App tai Meta for Developers.
2. Them san pham Messenger.
3. Tao Page Access Token cho fanpage.
4. Trong Webhooks, chon Page va dien Callback URL:

```text
https://your-domain.com/webhook
```

5. Verify Token phai trung voi `App__MessengerVerifyToken`.
6. Subscribe event `messages`.
7. Gan app voi fanpage va bat Messenger webhook.

## Cach hoat dong

1. User nhan tin cho fanpage.
2. Facebook gui event den `POST /webhook`.
3. App lay `sender.id` va `message.text`.
4. App goi OpenAI Responses API voi model `gpt-4o-mini`.
5. App gui cau tra loi ve Messenger qua Graph API.

## Deploy

Co the deploy len Azure App Service, Render, Railway, VPS, Docker, hoac bat ky host nao chay ASP.NET Core. Nho cau hinh HTTPS public URL va cac environment variables o tren.

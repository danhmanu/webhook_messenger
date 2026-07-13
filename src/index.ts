import { Container, getRandom } from "@cloudflare/containers";

const INSTANCE_COUNT = 2;

export class MessengerWebhookContainer extends Container {
  defaultPort = 8080;
  sleepAfter = "10m";
}

export interface Env {
  MESSENGER_WEBHOOK_CONTAINER: DurableObjectNamespace<MessengerWebhookContainer>;
  OPENAI_API_KEY: string;
  OPENAI_MODEL: string;
  MESSENGER_VERIFY_TOKEN: string;
  MESSENGER_PAGE_ACCESS_TOKEN: string;
  SYSTEM_PROMPT: string;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const container = await getRandom(env.MESSENGER_WEBHOOK_CONTAINER, INSTANCE_COUNT);

    await container.startAndWaitForPorts({
      startOptions: {
        envVars: {
          ASPNETCORE_ENVIRONMENT: "Production",
          ASPNETCORE_URLS: "http://+:8080",
          "App__OpenAiApiKey": env.OPENAI_API_KEY,
          "App__OpenAiModel": env.OPENAI_MODEL,
          "App__MessengerVerifyToken": env.MESSENGER_VERIFY_TOKEN,
          "App__MessengerPageAccessToken": env.MESSENGER_PAGE_ACCESS_TOKEN,
          "App__SystemPrompt": env.SYSTEM_PROMPT
        }
      }
    });

    return container.fetch(request);
  }
};
